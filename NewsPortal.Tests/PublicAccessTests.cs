using System.Net;
using NewsPortal.Models;
using NewsPortal.Tests.Infrastructure;

namespace NewsPortal.Tests;

public class PublicAccessTests
{
    [Fact]
    public async Task HomePage_ShowsOnlyPublishedArticles()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        await app.AddArticleAsync(author.Id, ArticleStatus.Published, title: "Опубликованная новость");
        await app.AddArticleAsync(author.Id, ArticleStatus.Pending, title: "Статья на модерации");
        await app.AddArticleAsync(author.Id, ArticleStatus.Rejected, title: "Отклонённая статья");

        var html = await app.CreateBrowser().GetStringAsync("/");

        Assert.Contains("Опубликованная новость", html);
        Assert.Contains(author.DisplayName, html);
        Assert.DoesNotContain("Статья на модерации", html);
        Assert.DoesNotContain("Отклонённая статья", html);
    }

    [Theory]
    [InlineData(ArticleStatus.Pending)]
    [InlineData(ArticleStatus.Rejected)]
    public async Task ArticlePage_NotPublished_Returns404(ArticleStatus status)
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var article = await app.AddArticleAsync(author.Id, status);

        var response = await app.CreateBrowser().GetAsync($"/Home/Article/{article.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArticlePage_Published_ShowsFullText()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Published, content: "Полный текст новости");

        var html = await app.CreateBrowser().GetStringAsync($"/Home/Article/{article.Id}");

        Assert.Contains("Полный текст новости", html);
    }

    [Theory]
    [InlineData("/MyArticles")]
    [InlineData("/MyArticles/Create")]
    [InlineData("/MyArticles/Edit/1")]
    [InlineData("/Moderation")]
    [InlineData("/Moderation/Review/1")]
    [InlineData("/Users")]
    [InlineData("/Users/Create")]
    public async Task ProtectedPages_Anonymous_RedirectToLogin(string url)
    {
        await using var app = await TestApp.CreateAsync();

        var response = await app.CreateBrowser().GetAsync(url);

        response.AssertRedirectsTo("/Account/Login");
    }

    [Fact]
    public async Task PublicRegistration_DoesNotExist()
    {
        await using var app = await TestApp.CreateAsync();
        var browser = app.CreateBrowser();

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/Account/Register")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/Identity/Account/Register")).StatusCode);
    }

    [Fact]
    public async Task ArticleContent_IsHtmlEncoded()
    {
        await using var app = await TestApp.CreateAsync();
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Published,
            title: "<b>Жирный</b>", content: "<script>alert('xss')</script>");

        var html = await app.CreateBrowser().GetStringAsync($"/Home/Article/{article.Id}");

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;alert(", html);
        Assert.DoesNotContain("<b>Жирный</b>", html);
    }

    [Fact]
    public async Task Responses_HaveSecurityHeaders()
    {
        await using var app = await TestApp.CreateAsync();

        var response = await app.CreateBrowser().GetAsync("/");

        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task BehindHttpsProxy_SendsHsts()
    {
        await using var app = await TestApp.CreateAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        // Так Render передаёт запрос, пришедший к нему по HTTPS.
        request.Headers.Host = "newsportal.example";
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await app.CreateBrowser().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        await using var app = await TestApp.CreateAsync();

        var response = await app.CreateBrowser().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_WrongPassword_ShowsError()
    {
        await using var app = await TestApp.CreateAsync();
        await app.CreateUserAsync("author@test.local", Roles.Author);

        var response = await app.CreateBrowser().SubmitFormAsync("/Account/Login", "/Account/Login", new()
        {
            ["Email"] = "author@test.local",
            ["Password"] = "Wrong-Passw0rd"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Неверный email или пароль.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_ExternalReturnUrl_IsIgnored()
    {
        await using var app = await TestApp.CreateAsync();
        await app.CreateUserAsync("author@test.local", Roles.Author);

        var response = await app.CreateBrowser().SubmitFormAsync("/Account/Login",
            "/Account/Login?returnUrl=https%3A%2F%2Fevil.example", new()
            {
                ["Email"] = "author@test.local",
                ["Password"] = TestApp.Password
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("evil.example", response.Headers.Location!.OriginalString);
    }
}
