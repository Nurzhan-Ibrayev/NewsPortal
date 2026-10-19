using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewsPortal.Data;
using NewsPortal.Models;

namespace NewsPortal.Tests.Infrastructure;

// Запускает настоящее приложение в памяти (вместе с Identity, авторизацией и CSRF),
// но вместо Neon использует отдельную базу EF InMemory для каждого теста.
public class TestApp : WebApplicationFactory<Program>
{
    public const string Password = "Test-Passw0rd";

    private readonly string _databaseName = "NewsPortalTests-" + Guid.NewGuid();

    static TestApp()
    {
        // Program.cs требует строку подключения. Реального подключения не будет:
        // ниже Npgsql заменяется на InMemory.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Host=unused;Database=unused");
    }

    public static async Task<TestApp> CreateAsync()
    {
        var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }

        return app;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Не Development: user-secrets с настоящей строкой Neon не загружаются.
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    public async Task<ApplicationUser> CreateUserAsync(string email, string role, bool isBlocked = false)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = email.Split('@')[0],
            IsBlocked = isBlocked
        };

        var result = await userManager.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    public async Task<Article> AddArticleAsync(
        string authorId,
        ArticleStatus status,
        string title = "Заголовок",
        string content = "Текст статьи",
        string? rejectionReason = null)
    {
        return await WithDbAsync(async db =>
        {
            var article = new Article
            {
                AuthorId = authorId,
                Title = title,
                Content = content,
                Status = status,
                RejectionReason = rejectionReason,
                PublishedAt = status == ArticleStatus.Published ? DateTime.UtcNow : null
            };
            db.Articles.Add(article);
            await db.SaveChangesAsync();
            return article;
        });
    }

    public Task<Article> GetArticleAsync(int id) =>
        WithDbAsync(db => db.Articles.AsNoTracking().SingleAsync(a => a.Id == id));

    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(db);
    }

    // «Браузер»: хранит cookie и не следует редиректам автоматически,
    // чтобы тест видел, куда именно отправляет сервер.
    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<HttpClient> LoginAsync(string email)
    {
        var browser = CreateBrowser();
        var response = await browser.SubmitFormAsync("/Account/Login", "/Account/Login", new()
        {
            ["Email"] = email,
            ["Password"] = Password
        });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        return browser;
    }
}
