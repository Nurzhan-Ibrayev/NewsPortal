using Microsoft.EntityFrameworkCore;
using NewsPortal.Data;
using NewsPortal.Models;
using NewsPortal.Services;

namespace NewsPortal.Tests;

// Модульные тесты правил смены статуса — без HTTP, напрямую через сервис.
public class ArticleServiceTests
{
    private const string AuthorId = "author-1";
    private const string ModeratorId = "moderator-1";

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ArticleServiceTests-" + Guid.NewGuid())
            .Options);

    [Fact]
    public async Task Create_SetsPendingStatus()
    {
        await using var db = CreateDb();
        var service = new ArticleService(db);

        var article = await service.CreateAsync(AuthorId, "  Заголовок  ", "Текст");

        Assert.Equal(ArticleStatus.Pending, article.Status);
        Assert.Equal("Заголовок", article.Title);
        Assert.Null(article.PublishedAt);
    }

    [Fact]
    public async Task FullCycle_RejectResubmitPublish()
    {
        await using var db = CreateDb();
        var service = new ArticleService(db);
        var article = await service.CreateAsync(AuthorId, "Заголовок", "Текст");

        Assert.Equal(ArticleOperationResult.Success, await service.RejectAsync(article.Id, ModeratorId, "Нужны источники"));
        Assert.Equal(ArticleStatus.Rejected, article.Status);

        Assert.Equal(ArticleOperationResult.Success, await service.ResubmitAsync(article.Id, AuthorId, "Заголовок", "Текст с источниками"));
        Assert.Equal(ArticleStatus.Pending, article.Status);

        Assert.Equal(ArticleOperationResult.Success, await service.PublishAsync(article.Id, ModeratorId));
        Assert.Equal(ArticleStatus.Published, article.Status);
        Assert.Equal(ModeratorId, article.ReviewedById);
        Assert.NotNull(article.PublishedAt);
        Assert.Null(article.RejectionReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_RequiresReason(string? reason)
    {
        await using var db = CreateDb();
        var service = new ArticleService(db);
        var article = await service.CreateAsync(AuthorId, "Заголовок", "Текст");

        var result = await service.RejectAsync(article.Id, ModeratorId, reason);

        Assert.Equal(ArticleOperationResult.ReasonRequired, result);
        Assert.Equal(ArticleStatus.Pending, article.Status);
    }

    [Theory]
    [InlineData(ArticleStatus.Rejected)]
    [InlineData(ArticleStatus.Published)]
    public async Task Moderation_OnlyFromPending(ArticleStatus status)
    {
        await using var db = CreateDb();
        var service = new ArticleService(db);
        var article = await service.CreateAsync(AuthorId, "Заголовок", "Текст");
        article.Status = status;
        await db.SaveChangesAsync();

        Assert.Equal(ArticleOperationResult.InvalidStatus, await service.PublishAsync(article.Id, ModeratorId));
        Assert.Equal(ArticleOperationResult.InvalidStatus, await service.RejectAsync(article.Id, ModeratorId, "Причина"));
        Assert.Equal(status, article.Status);
    }

    [Theory]
    [InlineData(ArticleStatus.Pending)]
    [InlineData(ArticleStatus.Published)]
    public async Task Resubmit_OnlyFromRejected(ArticleStatus status)
    {
        await using var db = CreateDb();
        var service = new ArticleService(db);
        var article = await service.CreateAsync(AuthorId, "Исходный", "Текст");
        article.Status = status;
        await db.SaveChangesAsync();

        var result = await service.ResubmitAsync(article.Id, AuthorId, "Новый", "Новый текст");

        Assert.Equal(ArticleOperationResult.InvalidStatus, result);
        Assert.Equal("Исходный", article.Title);
    }

    [Fact]
    public async Task Resubmit_OtherAuthor_NotFound()
    {
        await using var db = CreateDb();
        var service = new ArticleService(db);
        var article = await service.CreateAsync(AuthorId, "Заголовок", "Текст");
        await service.RejectAsync(article.Id, ModeratorId, "Причина");

        var result = await service.ResubmitAsync(article.Id, "someone-else", "Взлом", "Взлом");

        Assert.Equal(ArticleOperationResult.NotFound, result);
        Assert.Equal(ArticleStatus.Rejected, article.Status);
    }

    [Fact]
    public async Task Moderator_CannotModerateOwnArticle()
    {
        await using var db = CreateDb();
        var service = new ArticleService(db);
        var article = await service.CreateAsync(ModeratorId, "Заголовок", "Текст");

        Assert.Equal(ArticleOperationResult.OwnArticle, await service.PublishAsync(article.Id, ModeratorId));
        Assert.Equal(ArticleStatus.Pending, article.Status);
    }

    [Fact]
    public async Task PublishedList_ExcludesOtherStatuses()
    {
        await using var db = CreateDb();
        // Список подгружает автора (Include), поэтому автор должен существовать — как и в реальной БД.
        db.Users.Add(new ApplicationUser { Id = AuthorId, UserName = "author", DisplayName = "Автор" });
        await db.SaveChangesAsync();
        var service = new ArticleService(db);
        var pending = await service.CreateAsync(AuthorId, "На модерации", "Текст");
        var published = await service.CreateAsync(AuthorId, "Опубликована", "Текст");
        await service.PublishAsync(published.Id, ModeratorId);

        var (articles, total) = await service.GetPublishedPageAsync(1, 10);

        Assert.Equal(1, total);
        Assert.Equal(published.Id, Assert.Single(articles).Id);
        Assert.Null(await service.GetPublishedAsync(pending.Id));
    }
}
