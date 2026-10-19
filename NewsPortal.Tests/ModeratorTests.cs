using System.Net;
using NewsPortal.Models;
using NewsPortal.Tests.Infrastructure;

namespace NewsPortal.Tests;

public class ModeratorTests
{
    [Fact]
    public async Task Queue_ShowsPendingArticles()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var moderator = await app.CreateUserAsync("moderator@test.local", Roles.Moderator);
        await app.AddArticleAsync(author.Id, ArticleStatus.Pending, title: "Ждёт проверки");
        await app.AddArticleAsync(author.Id, ArticleStatus.Published, title: "Уже опубликована");
        var browser = await app.LoginAsync(moderator.Email!);

        var html = await browser.GetStringAsync("/Moderation");

        Assert.Contains("Ждёт проверки", html);
        Assert.DoesNotContain("Уже опубликована", html);
    }

    [Fact]
    public async Task Publish_PendingArticle_BecomesPublic()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var moderator = await app.CreateUserAsync("moderator@test.local", Roles.Moderator);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Pending, title: "Новость дня");
        var browser = await app.LoginAsync(moderator.Email!);

        var review = await browser.GetStringAsync($"/Moderation/Review/{article.Id}");
        Assert.Contains("Новость дня", review);

        var response = await browser.SubmitFormAsync($"/Moderation/Publish/{article.Id}");

        response.AssertRedirectsTo("/Moderation");
        var published = await app.GetArticleAsync(article.Id);
        Assert.Equal(ArticleStatus.Published, published.Status);
        Assert.NotNull(published.PublishedAt);
        Assert.Equal(moderator.Id, published.ReviewedById);
        Assert.Contains("Новость дня", await app.CreateBrowser().GetStringAsync("/"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_WithoutReason_IsNotAllowed(string reason)
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var moderator = await app.CreateUserAsync("moderator@test.local", Roles.Moderator);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Pending);
        var browser = await app.LoginAsync(moderator.Email!);

        var response = await browser.SubmitFormAsync($"/Moderation/Reject/{article.Id}", new()
        {
            ["Reject.RejectionReason"] = reason
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Укажите причину отклонения.", await response.Content.ReadAsStringAsync());
        Assert.Equal(ArticleStatus.Pending, (await app.GetArticleAsync(article.Id)).Status);
    }

    [Fact]
    public async Task Reject_WithReason_AuthorSeesReason()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var moderator = await app.CreateUserAsync("moderator@test.local", Roles.Moderator);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Pending);
        var browser = await app.LoginAsync(moderator.Email!);

        var response = await browser.SubmitFormAsync($"/Moderation/Reject/{article.Id}", new()
        {
            ["Reject.RejectionReason"] = "Нет ссылок на источники"
        });

        response.AssertRedirectsTo("/Moderation");
        var rejected = await app.GetArticleAsync(article.Id);
        Assert.Equal(ArticleStatus.Rejected, rejected.Status);
        Assert.Equal("Нет ссылок на источники", rejected.RejectionReason);
        Assert.Equal(moderator.Id, rejected.ReviewedById);

        var authorBrowser = await app.LoginAsync(author.Email!);
        Assert.Contains("Нет ссылок на источники", await authorBrowser.GetStringAsync($"/MyArticles/Details/{article.Id}"));
    }

    [Fact]
    public async Task RejectedArticle_CannotBePublishedDirectly()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var moderator = await app.CreateUserAsync("moderator@test.local", Roles.Moderator);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Rejected, rejectionReason: "Причина");
        var browser = await app.LoginAsync(moderator.Email!);

        var response = await browser.SubmitFormAsync($"/Moderation/Publish/{article.Id}");

        response.AssertRedirectsTo($"/Moderation/Review/{article.Id}");
        Assert.Equal(ArticleStatus.Rejected, (await app.GetArticleAsync(article.Id)).Status);
    }

    [Theory]
    [InlineData("/Users")]
    [InlineData("/Users/Create")]
    [InlineData("/MyArticles")]
    [InlineData("/MyArticles/Create")]
    public async Task AdminAndAuthorPages_AreForbidden(string url)
    {
        await using var app = await TestApp.CreateAsync();
        var moderator = await app.CreateUserAsync("moderator@test.local", Roles.Moderator);
        var browser = await app.LoginAsync(moderator.Email!);

        var response = await browser.GetAsync(url);

        response.AssertRedirectsTo("/Account/AccessDenied");
    }

    [Fact]
    public async Task CannotEditArticleContent()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var moderator = await app.CreateUserAsync("moderator@test.local", Roles.Moderator);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Rejected, title: "Исходный", rejectionReason: "Причина");
        var browser = await app.LoginAsync(moderator.Email!);

        // Форма редактирования доступна только автору.
        var response = await browser.SubmitFormAsync($"/MyArticles/Edit/{article.Id}", new()
        {
            ["Title"] = "Изменено модератором",
            ["Content"] = "Изменено модератором"
        });

        response.AssertRedirectsTo("/Account/AccessDenied");
        Assert.Equal("Исходный", (await app.GetArticleAsync(article.Id)).Title);
    }
}
