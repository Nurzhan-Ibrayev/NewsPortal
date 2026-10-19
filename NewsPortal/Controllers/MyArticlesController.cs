using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NewsPortal.Models;
using NewsPortal.Services;
using NewsPortal.ViewModels;

namespace NewsPortal.Controllers;

// Кабинет автора. AuthorId всегда берётся из cookie текущего пользователя, а не из формы или URL.
[Authorize(Roles = Roles.Author)]
public class MyArticlesController : Controller
{
    private readonly ArticleService _articles;
    private readonly UserManager<ApplicationUser> _userManager;

    public MyArticlesController(ArticleService articles, UserManager<ApplicationUser> userManager)
    {
        _articles = articles;
        _userManager = userManager;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        return View(await _articles.GetAuthorArticlesAsync(CurrentUserId));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        // Чужая статья не найдётся: запрос фильтрует по автору.
        var article = await _articles.GetAuthorArticleAsync(id, CurrentUserId);
        if (article is null)
        {
            return NotFound();
        }

        return View(article);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new ArticleFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ArticleFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var article = await _articles.CreateAsync(CurrentUserId, model.Title, model.Content);

        TempData["Success"] = "Статья отправлена на модерацию.";
        return RedirectToAction(nameof(Details), new { id = article.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var article = await _articles.GetAuthorArticleAsync(id, CurrentUserId);
        if (article is null)
        {
            return NotFound();
        }

        if (article.Status != ArticleStatus.Rejected)
        {
            TempData["Error"] = "Редактировать можно только отклонённую статью.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(new EditArticleViewModel
        {
            Id = article.Id,
            Title = article.Title,
            Content = article.Content,
            RejectionReason = article.RejectionReason
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ArticleFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var article = await _articles.GetAuthorArticleAsync(id, CurrentUserId);
            if (article is null)
            {
                return NotFound();
            }

            if (article.Status != ArticleStatus.Rejected)
            {
                TempData["Error"] = "Редактировать можно только отклонённую статью.";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(new EditArticleViewModel
            {
                Id = article.Id,
                Title = model.Title,
                Content = model.Content,
                RejectionReason = article.RejectionReason
            });
        }

        var result = await _articles.ResubmitAsync(id, CurrentUserId, model.Title, model.Content);

        switch (result)
        {
            case ArticleOperationResult.Success:
                TempData["Success"] = "Статья исправлена и повторно отправлена на модерацию.";
                break;
            case ArticleOperationResult.NotFound:
                return NotFound();
            case ArticleOperationResult.InvalidStatus:
                TempData["Error"] = "Редактировать можно только отклонённую статью.";
                break;
            default:
                TempData["Error"] = "Статья была изменена одновременно с вашим запросом. Проверьте её и повторите.";
                break;
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
