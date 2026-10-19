using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NewsPortal.Models;

namespace NewsPortal.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Article> Articles => Set<Article>();

    // Ключи шифрования cookie входа и antiforgery-токенов. Хранятся в БД, а не в контейнере,
    // чтобы пользователей не разлогинивало при перезапуске или новом деплое на Render.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Сначала базовая настройка таблиц Identity.
        base.OnModelCreating(builder);

        builder.Entity<Article>(article =>
        {
            // Храним статус строкой ("Pending"), так данные понятнее при просмотре БД.
            article.Property(a => a.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            // Пользователей не удаляем (только блокируем), поэтому Restrict:
            // нельзя случайно удалить автора вместе с его статьями.
            article.HasOne(a => a.Author)
                .WithMany()
                .HasForeignKey(a => a.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            article.HasOne(a => a.ReviewedBy)
                .WithMany()
                .HasForeignKey(a => a.ReviewedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Публичная лента и очередь модерации выбирают статьи по статусу.
            article.HasIndex(a => new { a.Status, a.PublishedAt });
        });
    }
}
