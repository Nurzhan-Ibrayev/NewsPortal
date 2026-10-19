using System.ComponentModel.DataAnnotations;
using NewsPortal.Models;

namespace NewsPortal.ViewModels;

// Форма автора содержит только заголовок и текст.
// Статуса, AuthorId и ReviewedById здесь нет — подставить их через форму невозможно.
public class ArticleFormViewModel
{
    [Required(ErrorMessage = "Введите заголовок.")]
    [StringLength(Article.TitleMaxLength, ErrorMessage = "Заголовок не должен быть длиннее {1} символов.")]
    [Display(Name = "Заголовок")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите текст статьи.")]
    [StringLength(Article.ContentMaxLength, ErrorMessage = "Текст не должен быть длиннее {1} символов.")]
    [Display(Name = "Текст")]
    public string Content { get; set; } = string.Empty;
}

// Для страницы редактирования: те же поля формы плюс данные только для показа.
// Контроллер принимает из запроса ArticleFormViewModel, поэтому Id и причину
// нельзя подменить — они всегда берутся из БД.
public class EditArticleViewModel : ArticleFormViewModel
{
    public int Id { get; set; }
    public string? RejectionReason { get; set; }
}

public class RejectArticleViewModel
{
    [Required(ErrorMessage = "Укажите причину отклонения.")]
    [StringLength(Article.RejectionReasonMaxLength, ErrorMessage = "Причина не должна быть длиннее {1} символов.")]
    [Display(Name = "Причина отклонения")]
    public string RejectionReason { get; set; } = string.Empty;
}

public class ArticleReviewViewModel
{
    public required Article Article { get; set; }
    public RejectArticleViewModel Reject { get; set; } = new();
}

public class ModerationListViewModel
{
    public ArticleStatus Status { get; set; }
    public List<Article> Articles { get; set; } = [];
}

public class PublicArticleListViewModel
{
    public List<Article> Articles { get; set; } = [];
    public int Page { get; set; }
    public int TotalPages { get; set; }
}
