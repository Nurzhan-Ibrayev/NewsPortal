using System.ComponentModel.DataAnnotations;

namespace NewsPortal.Models;

public class Article
{
    public const int TitleMaxLength = 200;
    public const int ContentMaxLength = 20000;
    public const int RejectionReasonMaxLength = 1000;

    public int Id { get; set; }

    [Required]
    [MaxLength(TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(ContentMaxLength)]
    public string Content { get; set; } = string.Empty;

    [Required]
    public string AuthorId { get; set; } = string.Empty;
    public ApplicationUser? Author { get; set; }

    public ArticleStatus Status { get; set; } = ArticleStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    [MaxLength(RejectionReasonMaxLength)]
    public string? RejectionReason { get; set; }

    public string? ReviewedById { get; set; }
    public ApplicationUser? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // Системная колонка PostgreSQL xmin: защищает от одновременного изменения
    // статьи двумя пользователями (например, два модератора сразу).
    [Timestamp]
    public uint Version { get; set; }

    // Краткое содержание для списков: начало текста, обрезанное по границе слова.
    public string GetExcerpt(int maxLength = 300)
    {
        if (Content.Length <= maxLength)
        {
            return Content;
        }

        var cut = Content.LastIndexOf(' ', maxLength);
        return Content[..(cut > 0 ? cut : maxLength)].TrimEnd() + "…";
    }
}
