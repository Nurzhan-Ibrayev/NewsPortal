using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsPortal.Models;
using NewsPortal.Tests.Infrastructure;

namespace NewsPortal.Tests;

public class AdminTests
{
    [Fact]
    public async Task CreateUser_WithRole_CanLogIn()
    {
        await using var app = await TestApp.CreateAsync();
        var admin = await app.CreateUserAsync("admin@test.local", Roles.Admin);
        var browser = await app.LoginAsync(admin.Email!);

        var response = await browser.SubmitFormAsync("/Users/Create", "/Users/Create", new()
        {
            ["DisplayName"] = "Новый модератор",
            ["Email"] = "newmod@test.local",
            ["Password"] = TestApp.Password,
            ["ConfirmPassword"] = TestApp.Password,
            ["Role"] = Roles.Moderator
        });

        response.AssertRedirectsTo("/Users");
        Assert.Contains("newmod@test.local", await browser.GetStringAsync("/Users"));
        Assert.Equal([Roles.Moderator], await GetRolesAsync(app, "newmod@test.local"));

        // Пароль хранится только как хеш.
        var stored = await app.WithDbAsync(db => db.Users.SingleAsync(u => u.Email == "newmod@test.local"));
        Assert.NotEqual(TestApp.Password, stored.PasswordHash);

        var newModerator = await app.LoginAsync("newmod@test.local");
        Assert.Equal(HttpStatusCode.OK, (await newModerator.GetAsync("/Moderation")).StatusCode);
    }

    [Fact]
    public async Task CreateUser_UnknownRole_IsRejected()
    {
        await using var app = await TestApp.CreateAsync();
        var admin = await app.CreateUserAsync("admin@test.local", Roles.Admin);
        var browser = await app.LoginAsync(admin.Email!);

        var response = await browser.SubmitFormAsync("/Users/Create", "/Users/Create", new()
        {
            ["DisplayName"] = "Хакер",
            ["Email"] = "hacker@test.local",
            ["Password"] = TestApp.Password,
            ["ConfirmPassword"] = TestApp.Password,
            ["Role"] = "SuperAdmin"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Выберите роль из списка.", await response.Content.ReadAsStringAsync());
        Assert.False(await app.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == "hacker@test.local")));
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_ShowsError()
    {
        await using var app = await TestApp.CreateAsync();
        var admin = await app.CreateUserAsync("admin@test.local", Roles.Admin);
        await app.CreateUserAsync("author@test.local", Roles.Author);
        var browser = await app.LoginAsync(admin.Email!);

        var response = await browser.SubmitFormAsync("/Users/Create", "/Users/Create", new()
        {
            ["DisplayName"] = "Дубликат",
            ["Email"] = "author@test.local",
            ["Password"] = TestApp.Password,
            ["ConfirmPassword"] = TestApp.Password,
            ["Role"] = Roles.Author
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("уже", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EditUser_ChangesRole()
    {
        await using var app = await TestApp.CreateAsync();
        var admin = await app.CreateUserAsync("admin@test.local", Roles.Admin);
        var user = await app.CreateUserAsync("author@test.local", Roles.Author);
        var browser = await app.LoginAsync(admin.Email!);

        var response = await browser.SubmitFormAsync($"/Users/Edit/{user.Id}", $"/Users/Edit/{user.Id}", new()
        {
            ["Id"] = user.Id,
            ["DisplayName"] = "Теперь модератор",
            ["Email"] = "author@test.local",
            ["Role"] = Roles.Moderator
        });

        response.AssertRedirectsTo("/Users");
        Assert.Equal([Roles.Moderator], await GetRolesAsync(app, "author@test.local"));
    }

    [Fact]
    public async Task EditUser_CannotRemoveOwnAdminRole()
    {
        await using var app = await TestApp.CreateAsync();
        var admin = await app.CreateUserAsync("admin@test.local", Roles.Admin);
        var browser = await app.LoginAsync(admin.Email!);

        var response = await browser.SubmitFormAsync($"/Users/Edit/{admin.Id}", $"/Users/Edit/{admin.Id}", new()
        {
            ["Id"] = admin.Id,
            ["DisplayName"] = "Админ",
            ["Email"] = "admin@test.local",
            ["Role"] = Roles.Author
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Нельзя снять роль администратора с самого себя.", await response.Content.ReadAsStringAsync());
        Assert.Equal([Roles.Admin], await GetRolesAsync(app, "admin@test.local"));
    }

    [Fact]
    public async Task BlockUser_EndsActiveSessionAndPreventsLogin()
    {
        await using var app = await TestApp.CreateAsync();
        var admin = await app.CreateUserAsync("admin@test.local", Roles.Admin);
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var authorBrowser = await app.LoginAsync(author.Email!);
        Assert.Equal(HttpStatusCode.OK, (await authorBrowser.GetAsync("/MyArticles")).StatusCode);

        var adminBrowser = await app.LoginAsync(admin.Email!);
        var response = await adminBrowser.SubmitFormAsync($"/Users/Block/{author.Id}");
        response.AssertRedirectsTo("/Users");

        // Уже открытая сессия автора больше не действует.
        (await authorBrowser.GetAsync("/MyArticles")).AssertRedirectsTo("/Account/Login");

        // И войти заново нельзя, даже с правильным паролем.
        var login = await app.CreateBrowser().SubmitFormAsync("/Account/Login", "/Account/Login", new()
        {
            ["Email"] = author.Email!,
            ["Password"] = TestApp.Password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Учётная запись заблокирована", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnblockUser_AllowsLoginAgain()
    {
        await using var app = await TestApp.CreateAsync();
        var admin = await app.CreateUserAsync("admin@test.local", Roles.Admin);
        var author = await app.CreateUserAsync("author@test.local", Roles.Author, isBlocked: true);
        var adminBrowser = await app.LoginAsync(admin.Email!);

        var response = await adminBrowser.SubmitFormAsync($"/Users/Unblock/{author.Id}");

        response.AssertRedirectsTo("/Users");
        var authorBrowser = await app.LoginAsync(author.Email!);
        Assert.Equal(HttpStatusCode.OK, (await authorBrowser.GetAsync("/MyArticles")).StatusCode);
    }

    [Fact]
    public async Task CannotBlockSelf()
    {
        await using var app = await TestApp.CreateAsync();
        var admin = await app.CreateUserAsync("admin@test.local", Roles.Admin);
        var browser = await app.LoginAsync(admin.Email!);

        await browser.SubmitFormAsync($"/Users/Block/{admin.Id}");

        var stored = await app.WithDbAsync(db => db.Users.SingleAsync(u => u.Id == admin.Id));
        Assert.False(stored.IsBlocked);
    }

    [Fact]
    public async Task Admin_HasNoModerationRights()
    {
        await using var app = await TestApp.CreateAsync();
        var admin = await app.CreateUserAsync("admin@test.local", Roles.Admin);
        var author = await app.CreateUserAsync("author@test.local", Roles.Author);
        var article = await app.AddArticleAsync(author.Id, ArticleStatus.Pending);
        var browser = await app.LoginAsync(admin.Email!);

        (await browser.GetAsync("/Moderation")).AssertRedirectsTo("/Account/AccessDenied");
        (await browser.SubmitFormAsync($"/Moderation/Publish/{article.Id}")).AssertRedirectsTo("/Account/AccessDenied");
        Assert.Equal(ArticleStatus.Pending, (await app.GetArticleAsync(article.Id)).Status);
    }

    [Theory]
    [InlineData("/Users/Block/")]
    [InlineData("/Users/Unblock/")]
    public async Task NonAdmin_CannotBlockOrUnblock(string action)
    {
        await using var app = await TestApp.CreateAsync();
        var moderator = await app.CreateUserAsync("moderator@test.local", Roles.Moderator);
        var victim = await app.CreateUserAsync("author@test.local", Roles.Author);
        var browser = await app.LoginAsync(moderator.Email!);

        var response = await browser.SubmitFormAsync(action + victim.Id);

        response.AssertRedirectsTo("/Account/AccessDenied");
        var stored = await app.WithDbAsync(db => db.Users.SingleAsync(u => u.Id == victim.Id));
        Assert.False(stored.IsBlocked);
    }

    private static async Task<string[]> GetRolesAsync(TestApp app, string email)
    {
        using var scope = app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return [.. await userManager.GetRolesAsync(user!)];
    }
}
