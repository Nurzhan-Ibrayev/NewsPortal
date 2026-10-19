namespace NewsPortal.Models;

// Названия ролей в одном месте, чтобы не опечататься в [Authorize(Roles = ...)].
public static class Roles
{
    public const string Admin = "Admin";
    public const string Author = "Author";
    public const string Moderator = "Moderator";

    public static readonly string[] All = [Admin, Author, Moderator];

    public static bool IsValid(string? role) => role is not null && All.Contains(role);

    public static string GetDisplayName(string? role) => role switch
    {
        Admin => "Администратор",
        Author => "Автор",
        Moderator => "Модератор",
        _ => "Без роли"
    };
}
