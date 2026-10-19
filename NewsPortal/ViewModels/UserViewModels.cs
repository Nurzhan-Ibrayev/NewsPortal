using System.ComponentModel.DataAnnotations;

namespace NewsPortal.ViewModels;

public class UserListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Role { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCurrentUser { get; set; }
}

public class CreateUserViewModel
{
    [Required(ErrorMessage = "Введите имя.")]
    [StringLength(100, ErrorMessage = "Имя не должно быть длиннее {1} символов.")]
    [Display(Name = "Имя")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите email.")]
    [EmailAddress(ErrorMessage = "Некорректный email.")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите пароль.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Пароль должен содержать от {2} до {1} символов.")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Повторите пароль.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают.")]
    [Display(Name = "Повторите пароль")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Выберите роль.")]
    [Display(Name = "Роль")]
    public string Role { get; set; } = string.Empty;
}

public class EditUserViewModel
{
    // Id только определяет, кого редактируем. Страница доступна лишь Admin,
    // а пользователь всё равно заново загружается из БД на сервере.
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите имя.")]
    [StringLength(100, ErrorMessage = "Имя не должно быть длиннее {1} символов.")]
    [Display(Name = "Имя")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите email.")]
    [EmailAddress(ErrorMessage = "Некорректный email.")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Выберите роль.")]
    [Display(Name = "Роль")]
    public string Role { get; set; } = string.Empty;

    [StringLength(100, MinimumLength = 8, ErrorMessage = "Пароль должен содержать от {2} до {1} символов.")]
    [DataType(DataType.Password)]
    [Display(Name = "Новый пароль (необязательно)")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Пароли не совпадают.")]
    [Display(Name = "Повторите новый пароль")]
    public string? ConfirmNewPassword { get; set; }

    public bool IsBlocked { get; set; }
}
