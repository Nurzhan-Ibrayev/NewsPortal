using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NewsPortal.Models;
using NewsPortal.Services;
using NewsPortal.ViewModels;

namespace NewsPortal.Controllers;

// Публичная часть: доступна без входа и показывает только опубликованные статьи.
public class HomeController : Controller
{
    private const int PageSize = 10;

    private readonly ArticleService _articles;

    public HomeController(ArticleService articles)
    {
        _articles = articles;
    }

    public async Task<IActionResult> Index(int page = 1)
    {
        if (page < 1)
        {
            page = 1;
        }

        var (articles, total) = await _articles.GetPublishedPageAsync(page, PageSize);

        return View(new PublicArticleListViewModel
        {
            Articles = articles,
            Page = page,
            TotalPages = (int)Math.Ceiling(total / (double)PageSize)
        });
    }

    public async Task<IActionResult> Article(int id)
    {
        // Неопубликованная статья для публики не существует — 404, а не 403,
        // чтобы не раскрывать, что статья с таким номером есть.
        var article = await _articles.GetPublishedAsync(id);
        if (article is null)
        {
            return NotFound();
        }

        return View(article);
    }

    // Сюда перенаправляет UseStatusCodePagesWithReExecute: понятная страница для 400/404 и т.п.
    // Имя не StatusCode, чтобы не конфликтовать с методом Controller.StatusCode().
    // IgnoreAntiforgeryToken: при ошибке POST-запроса страница вызывается тем же POST
    // без токена; она ничего не меняет, поэтому проверка CSRF здесь не нужна.
    [IgnoreAntiforgeryToken]
    public IActionResult HttpStatus(int code)
    {
        Response.StatusCode = code;
        return View(code);
    }

    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
