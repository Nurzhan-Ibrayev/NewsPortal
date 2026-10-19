using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace NewsPortal.Models;

public enum ArticleStatus
{
    [Display(Name = "На модерации")]
    Pending = 0,

    [Display(Name = "Отклонена")]
    Rejected = 1,

    [Display(Name = "Опубликована")]
    Published = 2
}

public static class ArticleStatusExtensions
{
    // Русское название из атрибута [Display].
    public static string GetDisplayName(this ArticleStatus status) =>
        typeof(ArticleStatus).GetField(status.ToString())?
            .GetCustomAttribute<DisplayAttribute>()?.Name ?? status.ToString();

    // Цвет бейджа Bootstrap для статуса.
    public static string GetBadgeClass(this ArticleStatus status) => status switch
    {
        ArticleStatus.Pending => "bg-warning text-dark",
        ArticleStatus.Rejected => "bg-danger",
        ArticleStatus.Published => "bg-success",
        _ => "bg-secondary"
    };
}
