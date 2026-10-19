using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NewsPortal.Models;
using NewsPortal.Services;
using NewsPortal.ViewModels;

namespace NewsPortal.Controllers;

// Модерация. Модератор не может менять заголовок и текст —
// здесь есть только два действия, меняющие статус: Publish и Reject.
// Admin сюда не допускается: права модерации у него не подразумеваются.
[Authorize(Roles = Roles.Moderator)]
public class ModerationController : Controller
{
    private readonly ArticleService _articles;
    private readonly UserManager<ApplicationUser> _userManager;

    public ModerationController(ArticleService articles, UserManager<ApplicationUser> userManager)
    {
        _articles = articles;
        _userManager = userManager;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;

    [HttpGet]
    public async Task<IActionResult> Index(ArticleStatus status = ArticleStatus.Pending)
    {
        if (!Enum.IsDefined(status))
        {
            status = ArticleStatus.Pending;
        }

        return View(new ModerationListViewModel
        {
            Status = status,
            Articles = await _articles.GetByStatusAsync(status)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Review(int id)
    {
        var article = await _articles.GetForReviewAsync(id);
        if (article is null)
        {
            return NotFound();
        }

        return View(new ArticleReviewViewModel { Article = article });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id)
    {
        var result = await _articles.PublishAsync(id, CurrentUserId);
        if (result == ArticleOperationResult.NotFound)
        {
            return NotFound();
        }

        if (result == ArticleOperationResult.Success)
        {
            TempData["Success"] = "Статья опубликована.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Error"] = ErrorMessage(result);
        return RedirectToAction(nameof(Review), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, [Bind(Prefix = "Reject")] RejectArticleViewModel model)
    {
        if (!ModelState.IsValid)
        {
            // Показываем страницу проверки снова, с ошибкой под полем причины.
            var article = await _articles.GetForReviewAsync(id);
            if (article is null)
            {
                return NotFound();
            }

            return View(nameof(Review), new ArticleReviewViewModel { Article = article, Reject = model });
        }

        var result = await _articles.RejectAsync(id, CurrentUserId, model.RejectionReason);
        if (result == ArticleOperationResult.NotFound)
        {
            return NotFound();
        }

        if (result == ArticleOperationResult.Success)
        {
            TempData["Success"] = "Статья отклонена. Автор увидит причину.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Error"] = ErrorMessage(result);
        return RedirectToAction(nameof(Review), new { id });
    }

    private static string ErrorMessage(ArticleOperationResult result) => result switch
    {
        ArticleOperationResult.InvalidStatus => "Решение можно принять только по статье со статусом «На модерации».",
        ArticleOperationResult.OwnArticle => "Нельзя модерировать собственную статью.",
        ArticleOperationResult.ReasonRequired => "Укажите причину отклонения.",
        ArticleOperationResult.ConcurrencyConflict => "Статью только что изменил другой пользователь. Обновите страницу.",
        _ => "Не удалось выполнить действие."
    };
}
