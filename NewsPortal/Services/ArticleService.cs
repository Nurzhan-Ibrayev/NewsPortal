using Microsoft.EntityFrameworkCore;
using NewsPortal.Data;
using NewsPortal.Models;

namespace NewsPortal.Services;

public enum ArticleOperationResult
{
    Success,
    NotFound,
    InvalidStatus,
    OwnArticle,
    ReasonRequired,
    ConcurrencyConflict
}

// Все бизнес-правила жизненного цикла статьи собраны здесь:
//   создание          → На модерации
//   На модерации      → Опубликована (модератор)
//   На модерации      → Отклонена (модератор, причина обязательна)
//   Отклонена         → На модерации (автор исправил и отправил повторно)
// Других переходов нет: опубликованную статью автор не меняет,
// а отклонённую нельзя опубликовать, минуя повторную модерацию.
public class ArticleService
{
    private readonly ApplicationDbContext _db;

    public ArticleService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ---------- Публичная часть ----------

    public async Task<(List<Article> Articles, int TotalCount)> GetPublishedPageAsync(int page, int pageSize)
    {
        var query = _db.Articles
            .AsNoTracking()
            .Where(a => a.Status == ArticleStatus.Published);

        var total = await query.CountAsync();
        var articles = await query
            .Include(a => a.Author)
            .OrderByDescending(a => a.PublishedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (articles, total);
    }

    public Task<Article?> GetPublishedAsync(int id) =>
        _db.Articles
            .AsNoTracking()
            .Include(a => a.Author)
            .FirstOrDefaultAsync(a => a.Id == id && a.Status == ArticleStatus.Published);

    // ---------- Автор ----------
    // Во всех запросах автора есть условие AuthorId == authorId:
    // чужая статья для автора просто «не существует» (защита от IDOR).

    public Task<List<Article>> GetAuthorArticlesAsync(string authorId) =>
        _db.Articles
            .AsNoTracking()
            .Where(a => a.AuthorId == authorId)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync();

    public Task<Article?> GetAuthorArticleAsync(int id, string authorId) =>
        _db.Articles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.AuthorId == authorId);

    public async Task<Article> CreateAsync(string authorId, string title, string content)
    {
        var now = DateTime.UtcNow;
        var article = new Article
        {
            AuthorId = authorId,
            Title = title.Trim(),
            Content = content.Trim(),
            // Статус задаётся только сервером, из формы он не принимается.
            Status = ArticleStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Articles.Add(article);
        await _db.SaveChangesAsync();
        return article;
    }

    public async Task<ArticleOperationResult> ResubmitAsync(int id, string authorId, string title, string content)
    {
        var article = await _db.Articles.FirstOrDefaultAsync(a => a.Id == id && a.AuthorId == authorId);
        if (article is null)
        {
            return ArticleOperationResult.NotFound;
        }

        if (article.Status != ArticleStatus.Rejected)
        {
            return ArticleOperationResult.InvalidStatus;
        }

        article.Title = title.Trim();
        article.Content = content.Trim();
        article.Status = ArticleStatus.Pending;
        article.UpdatedAt = DateTime.UtcNow;
        // RejectionReason сохраняем: модератор увидит, что просили исправить.

        return await SaveAsync();
    }

    // ---------- Модератор ----------

    public Task<List<Article>> GetByStatusAsync(ArticleStatus status)
    {
        var query = _db.Articles
            .AsNoTracking()
            .Include(a => a.Author)
            .Where(a => a.Status == status);

        // Очередь модерации — от старых к новым (кто раньше отправил, того раньше проверят),
        // уже проверенные — от свежих решений к старым.
        query = status == ArticleStatus.Pending
            ? query.OrderBy(a => a.UpdatedAt)
            : query.OrderByDescending(a => a.ReviewedAt);

        return query.ToListAsync();
    }

    public Task<Article?> GetForReviewAsync(int id) =>
        _db.Articles
            .AsNoTracking()
            .Include(a => a.Author)
            .Include(a => a.ReviewedBy)
            .FirstOrDefaultAsync(a => a.Id == id);

    public async Task<ArticleOperationResult> PublishAsync(int id, string moderatorId)
    {
        var article = await _db.Articles.FirstOrDefaultAsync(a => a.Id == id);
        var check = CheckCanModerate(article, moderatorId);
        if (check != ArticleOperationResult.Success)
        {
            return check;
        }

        var now = DateTime.UtcNow;
        article!.Status = ArticleStatus.Published;
        article.PublishedAt = now;
        article.ReviewedById = moderatorId;
        article.ReviewedAt = now;
        article.RejectionReason = null;

        return await SaveAsync();
    }

    public async Task<ArticleOperationResult> RejectAsync(int id, string moderatorId, string? reason)
    {
        // Причина проверяется и здесь, а не только в форме: правило не обойти.
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ArticleOperationResult.ReasonRequired;
        }

        var article = await _db.Articles.FirstOrDefaultAsync(a => a.Id == id);
        var check = CheckCanModerate(article, moderatorId);
        if (check != ArticleOperationResult.Success)
        {
            return check;
        }

        article!.Status = ArticleStatus.Rejected;
        article.RejectionReason = reason.Trim();
        article.ReviewedById = moderatorId;
        article.ReviewedAt = DateTime.UtcNow;

        return await SaveAsync();
    }

    private static ArticleOperationResult CheckCanModerate(Article? article, string moderatorId)
    {
        if (article is null)
        {
            return ArticleOperationResult.NotFound;
        }

        // Решение принимается только по статье, ожидающей модерации.
        if (article.Status != ArticleStatus.Pending)
        {
            return ArticleOperationResult.InvalidStatus;
        }

        // На случай, если бывшему автору назначили роль модератора.
        if (article.AuthorId == moderatorId)
        {
            return ArticleOperationResult.OwnArticle;
        }

        return ArticleOperationResult.Success;
    }

    private async Task<ArticleOperationResult> SaveAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
            return ArticleOperationResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Статью успели изменить параллельно (например, другой модератор).
            return ArticleOperationResult.ConcurrencyConflict;
        }
    }
}
