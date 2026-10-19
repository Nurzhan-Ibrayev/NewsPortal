using System.Net;
using Microsoft.EntityFrameworkCore;
using NewsPortal.Models;
using NewsPortal.Tests.Infrastructure;

namespace NewsPortal.Tests;

public class AuthorTests
{
    [Fact]
    public async Task Create_SavesArticleAsPending()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var browser = await app.LoginAsync(author.Email!);

        var response = await browser.SubmitFormAsync("/MyArticles/Create", "/MyArticles/Create", new()
        {
            ["Title"] = "Новая статья",
            ["Content"] = "Текст"
        });

        response.AssertRedirectsTo("/MyArticles/Details/");
        var article = await app.WithDbAsync(db => db.Articles.SingleAsync());
        Assert.Equal(ArticleStatus.Pending, article.Status);
        Assert.Equal(author.Id, article.AuthorId);

        var list = await browser.GetStringAsync("/MyArticles");
        Assert.Contains("Новая статья", list);
        Assert.Contains("На модерации", list);
    }

    [Fact]
    public async Task Create_IgnoresStatusAndAuthorFromForm()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var other = await app.CreateUserAsync("other@test.local", Roles.Author);
        var browser = await app.LoginAsync(author.Email!);

        // Попытка подсунуть лишние поля, чтобы обойти модерацию.
        await browser.SubmitFormAsync("/MyArticles/Create", "/MyArticles/Create", new()
        {
            ["Title"] = "Хитрая статья",
            ["Content"] = "Текст",
            ["Status"] = "Published",
            ["AuthorId"] = other.Id,
            ["ReviewedById"] = other.Id
        });

        var article = await app.WithDbAsync(db => db.Articles.SingleAsync());
        Assert.Equal(ArticleStatus.Pending, article.Status);
        Assert.Equal(author.Id, article.AuthorId);
        Assert.Null(article.ReviewedById);
        Assert.Null(article.PublishedAt);
    }

    [Theory]
    [InlineData("", "Текст", "Введите заголовок.")]
    [InlineData("   ", "Текст", "Введите заголовок.")]
    [InlineData("Заголовок", "", "Введите текст статьи.")]
    public async Task Create_EmptyFields_ShowsValidationError(string title, string content, string expectedError)
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var browser = await app.LoginAsync(author.Email!);

        var response = await browser.SubmitFormAsync("/MyArticles/Create", "/MyArticles/Create", new()
        {
            ["Title"] = title,
            ["Content"] = content
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedError, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await app.WithDbAsync(db => db.Articles.CountAsync()));
    }

    [Fact]
    public async Task Create_WithoutAntiforgeryToken_IsRejected()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var browser = await app.LoginAsync(author.Email!);

        var response = await browser.PostAsync("/MyArticles/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = "Статья",
            ["Content"] = "Текст"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await app.WithDbAsync(db => db.Articles.CountAsync()));
    }

    [Fact]
    public async Task Index_ShowsOnlyOwnArticles()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var other = await app.CreateUserAsync("other@test.local", Roles.Author);
        await app.AddArticleAsync(author.Id, ArticleStatus.Pending, title: "Моя статья");
        await app.AddArticleAsync(other.Id, ArticleStatus.Pending, title: "Чужая статья");
        var browser = await app.LoginAsync(author.Email!);

        var html = await browser.GetStringAsync("/MyArticles");

        Assert.Contains("Моя статья", html);
        Assert.DoesNotContain("Чужая статья", html);
    }

    [Fact]
    public async Task OtherAuthorsArticle_CannotBeOpenedOrEdited()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var other = await app.CreateUserAsync("other@test.local", Roles.Author);
        var foreign = await app.AddArticleAsync(other.Id, ArticleStatus.Rejected, title: "Чужая", rejectionReason: "Причина");
        var browser = await app.LoginAsync(author.Email!);

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/MyArticles/Details/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/MyArticles/Edit/{foreign.Id}")).StatusCode);

        var post = await browser.SubmitFormAsync($"/MyArticles/Edit/{foreign.Id}", new()
        {
            ["Title"] = "Взлом",
            ["Content"] = "Взлом"
        });

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        var unchanged = await app.GetArticleAsync(foreign.Id);
        Assert.Equal("Чужая", unchanged.Title);
        Assert.Equal(ArticleStatus.Rejected, unchanged.Status);
    }

    [Fact]
    public async Task RejectedArticle_CanBeFixedAndResubmitted()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Rejected, rejectionReason: "Добавьте источники");
        var browser = await app.LoginAsync(author.Email!);

        var details = await browser.GetStringAsync($"/MyArticles/Details/{article.Id}");
        Assert.Contains("Добавьте источники", details);
        Assert.Contains("Отклонена", details);

        var response = await browser.SubmitFormAsync($"/MyArticles/Edit/{article.Id}", $"/MyArticles/Edit/{article.Id}", new()
        {
            ["Title"] = "Исправленный заголовок",
            ["Content"] = "Исправленный текст с источниками"
        });

        response.AssertRedirectsTo($"/MyArticles/Details/{article.Id}");
        var updated = await app.GetArticleAsync(article.Id);
        Assert.Equal(ArticleStatus.Pending, updated.Status);
        Assert.Equal("Исправленный заголовок", updated.Title);
    }

    [Theory]
    [InlineData(ArticleStatus.Pending)]
    [InlineData(ArticleStatus.Published)]
    public async Task NotRejectedArticle_CannotBeEdited(ArticleStatus status)
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var article = await app.AddArticleAsync(author.Id, status, title: "Исходный");
        var browser = await app.LoginAsync(author.Email!);

        var response = await browser.SubmitFormAsync($"/MyArticles/Edit/{article.Id}", new()
        {
            ["Title"] = "Изменённый",
            ["Content"] = "Изменённый"
        });

        response.AssertRedirectsTo($"/MyArticles/Details/{article.Id}");
        var unchanged = await app.GetArticleAsync(article.Id);
        Assert.Equal("Исходный", unchanged.Title);
        Assert.Equal(status, unchanged.Status);
    }

    [Theory]
    [InlineData("/Moderation")]
    [InlineData("/Moderation/Review/1")]
    [InlineData("/Users")]
    [InlineData("/Users/Create")]
    public async Task ModeratorAndAdminPages_AreForbidden(string url)
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var browser = await app.LoginAsync(author.Email!);

        var response = await browser.GetAsync(url);

        response.AssertRedirectsTo("/Account/AccessDenied");
    }

    [Fact]
    public async Task CannotPublishOwnArticle()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Pending);
        var browser = await app.LoginAsync(author.Email!);

        var response = await browser.SubmitFormAsync($"/Moderation/Publish/{article.Id}");

        response.AssertRedirectsTo("/Account/AccessDenied");
        Assert.Equal(ArticleStatus.Pending, (await app.GetArticleAsync(article.Id)).Status);
    }
}
