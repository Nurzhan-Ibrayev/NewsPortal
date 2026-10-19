using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsPortal.Data;
using NewsPortal.Models;
using NewsPortal.ViewModels;

namespace NewsPortal.Controllers;

// Управление пользователями. Весь контроллер доступен только роли Admin —
// проверка выполняется на сервере для каждого запроса, а не только скрытием ссылок.
[Authorize(Roles = Roles.Admin)]
public class UsersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public UsersController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        // Роли всех пользователей одним запросом, а не отдельным запросом на каждого.
        var userRoles = await (
                from ur in _db.UserRoles
                join r in _db.Roles on ur.RoleId equals r.Id
                select new { ur.UserId, r.Name })
            .ToListAsync();
        var roleByUserId = userRoles
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var currentUserId = _userManager.GetUserId(User);

        var model = users.Select(u => new UserListItemViewModel
        {
            Id = u.Id,
            DisplayName = u.DisplayName,
            Email = u.Email ?? string.Empty,
            Role = roleByUserId.GetValueOrDefault(u.Id),
            IsBlocked = u.IsBlocked,
            CreatedAt = u.CreatedAt,
            IsCurrentUser = u.Id == currentUserId
        }).ToList();

        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateUserViewModel { Role = Roles.Author });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        await ValidateRoleAsync(model.Role);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = model.DisplayName.Trim()
        };

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddErrors(createResult);
            return View(model);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, model.Role);
        if (!roleResult.Succeeded)
        {
            // Не оставляем пользователя без роли.
            await _userManager.DeleteAsync(user);
            AddErrors(roleResult);
            return View(model);
        }

        TempData["Success"] = $"Пользователь {user.Email} создан.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var model = new EditUserViewModel
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email ?? string.Empty,
            Role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty,
            IsBlocked = user.IsBlocked
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        var user = await _userManager.FindByIdAsync(model.Id);
        if (user is null)
        {
            return NotFound();
        }

        // Значение для отображения берём из БД, а не из формы.
        model.IsBlocked = user.IsBlocked;

        await ValidateRoleAsync(model.Role);

        var currentRoles = await _userManager.GetRolesAsync(user);
        var isSelf = user.Id == _userManager.GetUserId(User);

        if (currentRoles.Contains(Roles.Admin) && model.Role != Roles.Admin)
        {
            if (isSelf)
            {
                ModelState.AddModelError(nameof(model.Role), "Нельзя снять роль администратора с самого себя.");
            }
            else if (await IsLastActiveAdminAsync(user))
            {
                ModelState.AddModelError(nameof(model.Role), "Нельзя снять роль с последнего активного администратора.");
            }
        }

        // Проверяем новый пароль заранее, чтобы не сохранить изменения частично.
        if (!string.IsNullOrEmpty(model.NewPassword))
        {
            foreach (var validator in _userManager.PasswordValidators)
            {
                var passwordCheck = await validator.ValidateAsync(_userManager, user, model.NewPassword);
                foreach (var error in passwordCheck.Errors)
                {
                    ModelState.AddModelError(nameof(model.NewPassword), error.Description);
                }
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        user.DisplayName = model.DisplayName.Trim();
        user.Email = email;
        user.UserName = email;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            AddErrors(updateResult);
            return View(model);
        }

        if (!currentRoles.SequenceEqual([model.Role]))
        {
            // У пользователя всегда ровно одна роль. Смена роли меняет SecurityStamp,
            // поэтому пользователю придётся войти заново и права обновятся.
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            var addResult = removeResult.Succeeded
                ? await _userManager.AddToRoleAsync(user, model.Role)
                : removeResult;
            if (!addResult.Succeeded)
            {
                AddErrors(addResult);
                return View(model);
            }
        }

        if (!string.IsNullOrEmpty(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!passwordResult.Succeeded)
            {
                AddErrors(passwordResult);
                return View(model);
            }
        }

        if (isSelf)
        {
            // Обновляем собственную cookie, иначе администратор будет разлогинен.
            await _signInManager.RefreshSignInAsync(user);
        }

        TempData["Success"] = $"Данные пользователя {user.Email} сохранены.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Block(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "Нельзя заблокировать самого себя.";
            return RedirectToAction(nameof(Index));
        }

        if (await _userManager.IsInRoleAsync(user, Roles.Admin) && await IsLastActiveAdminAsync(user))
        {
            TempData["Error"] = "Нельзя заблокировать последнего активного администратора.";
            return RedirectToAction(nameof(Index));
        }

        user.IsBlocked = true;
        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded)
        {
            // Новый SecurityStamp делает недействительными уже выданные cookie:
            // заблокированный пользователь будет выкинут из системы при следующем запросе.
            result = await _userManager.UpdateSecurityStampAsync(user);
        }

        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"Пользователь {user.Email} заблокирован.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unblock(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        user.IsBlocked = false;
        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded)
        {
            // Заодно снимаем временную блокировку за неудачные попытки входа.
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);
        }

        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"Пользователь {user.Email} разблокирован.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateRoleAsync(string? role)
    {
        // Роль из формы принимаем только из фиксированного списка.
        if (!Roles.IsValid(role))
        {
            ModelState.AddModelError("Role", "Выберите роль из списка.");
        }
        else if (!await _roleManager.RoleExistsAsync(role!))
        {
            ModelState.AddModelError("Role", "Роль не найдена в базе данных. Выполните первоначальную настройку (seed-admin).");
        }
    }

    private async Task<bool> IsLastActiveAdminAsync(ApplicationUser user)
    {
        var admins = await _userManager.GetUsersInRoleAsync(Roles.Admin);
        return !admins.Any(a => a.Id != user.Id && !a.IsBlocked);
    }

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
