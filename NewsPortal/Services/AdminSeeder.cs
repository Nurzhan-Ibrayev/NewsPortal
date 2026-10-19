using Microsoft.AspNetCore.Identity;
using NewsPortal.Models;

namespace NewsPortal.Services;

// Первоначальная настройка: создаёт роли и первого администратора.
// Запускается вручную командой `dotnet run -- seed-admin`, а не при каждом старте сайта.
// Email и пароль берутся из конфигурации (user-secrets или переменные окружения
// AdminSeed__Email / AdminSeed__Password), поэтому в коде их нет.
// Повторный запуск безопасен: существующие роли и пользователь не дублируются.
public static class AdminSeeder
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AdminSeeder));
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = config["AdminSeed:Email"]?.Trim();
        var password = config["AdminSeed:Password"];
        var displayName = config["AdminSeed:DisplayName"]?.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogError("Не заданы AdminSeed:Email и/или AdminSeed:Password. Администратор не создан.");
            return 1;
        }

        foreach (var role in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
            if (!roleResult.Succeeded)
            {
                logger.LogError("Не удалось создать роль {Role}: {Errors}", role, Describe(roleResult));
                return 1;
            }

            logger.LogInformation("Создана роль {Role}.", role);
        }

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (await userManager.IsInRoleAsync(existing, Roles.Admin))
            {
                logger.LogInformation("Администратор {Email} уже существует. Изменения не требуются.", email);
                return 0;
            }

            // Не повышаем существующего пользователя до Admin молча.
            logger.LogError("Пользователь {Email} уже существует, но не является администратором. " +
                            "Укажите другой email или назначьте роль вручную.", email);
            return 1;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Администратор" : displayName
        };

        // Пароль проверяется правилами Identity и сохраняется только в виде хеша.
        var createResult = await userManager.CreateAsync(admin, password);
        if (!createResult.Succeeded)
        {
            logger.LogError("Не удалось создать администратора: {Errors}", Describe(createResult));
            return 1;
        }

        var addRoleResult = await userManager.AddToRoleAsync(admin, Roles.Admin);
        if (!addRoleResult.Succeeded)
        {
            await userManager.DeleteAsync(admin);
            logger.LogError("Не удалось назначить роль Admin: {Errors}", Describe(addRoleResult));
            return 1;
        }

        logger.LogInformation("Администратор {Email} создан.", email);
        return 0;
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));
}
