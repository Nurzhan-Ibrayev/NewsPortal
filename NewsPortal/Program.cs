using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;
using NewsPortal.Data;
using NewsPortal.Models;
using NewsPortal.Services;

var builder = WebApplication.CreateBuilder(args);

// Хостинги (например, Render) сообщают порт через переменную PORT.
// Если её нет, работают стандартные настройки (launchSettings.json локально, 8080 в Docker).
var port = builder.Configuration["PORT"];
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Строка подключения к Neon берётся из конфигурации:
// локально — из user-secrets, на Render — из переменной ConnectionStrings__DefaultConnection.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString) && !EF.IsDesignTime)
{
    throw new InvalidOperationException(
        "Не задана строка подключения 'ConnectionStrings:DefaultConnection'. " +
        "Укажите её через dotnet user-secrets или переменную окружения ConnectionStrings__DefaultConnection.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        // Neon «засыпает» без нагрузки и может оборвать соединение.
        // При кратковременном сбое EF повторит запрос, а не покажет пользователю ошибку.
        npgsql.EnableRetryOnFailure()));

// Ключи Data Protection (шифруют cookie входа и antiforgery-токены) хранятся в Neon,
// поэтому переживают перезапуск контейнера на Render.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetApplicationName("NewsPortal");

// Render принимает HTTPS на своём прокси и передаёт запрос в контейнер по HTTP
// с заголовками X-Forwarded-Proto / X-Forwarded-For. Без этой настройки приложение
// считало бы каждый запрос HTTP: ломались бы редирект на HTTPS, HSTS и флаг Secure у cookie.
// Адреса прокси Render заранее неизвестны, поэтому список доверенных прокси очищается.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;

        // Временная блокировка после 5 неудачных попыток входа (защита от подбора пароля).
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);

        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<RussianIdentityErrorDescriber>();

// Проверять SecurityStamp на каждом запросе. При блокировке или смене роли stamp меняется,
// и старая cookie сразу перестаёт действовать. Цена — один лёгкий запрос к БД на запрос.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

// Флаг Secure у служебных cookie, если запрос пришёл по HTTPS (на Render — всегда).
// Локально по HTTP cookie продолжают работать.
builder.Services.AddAntiforgery(options =>
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
builder.Services.Configure<CookieTempDataProviderOptions>(options =>
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);

builder.Services.AddScoped<ArticleService>();

// Простая проверка «сайт запущен» для платформы хостинга: GET /healthz → 200 Healthy.
builder.Services.AddHealthChecks();

builder.Services.AddControllersWithViews(options =>
{
    // Страховка: antiforgery-токен проверяется у всех POST-запросов,
    // даже если на действии забыли поставить [ValidateAntiForgeryToken].
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// Razor не будет превращать кириллицу в &#x...; коды. Спецсимволы HTML (<, >, &, ")
// по-прежнему экранируются, так что защита от XSS не ослабевает.
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic));

var app = builder.Build();

// Первоначальная настройка: `dotnet run -- seed-admin` создаёт роли и администратора и завершается.
if (args.Contains("seed-admin"))
{
    Environment.ExitCode = await AdminSeeder.RunAsync(app.Services);
    await app.DisposeAsync();
    return;
}

// Первым делом: восстановить исходную схему (https) и IP клиента из заголовков прокси.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Пустые ответы 400/404 заменяются понятной страницей (код ответа сохраняется).
app.UseStatusCodePagesWithReExecute("/Home/HttpStatus", "?code={0}");

// Заголовки безопасности для каждого ответа.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // CSP: скрипты и стили только с нашего сайта, встроенные <script> и onclick запрещены.
    // Даже если бы в HTML попал чужой скрипт, браузер его не выполнит.
    // data: в img-src нужен для иконок Bootstrap.
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "form-action 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
    await next();
});

// Редирект на HTTPS работает, когда известен HTTPS-порт (на Render: переменная HTTPS_PORT=443).
// /healthz не перенаправляем: проверку работоспособности платформа делает внутри сети по HTTP.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/healthz"),
    branch => branch.UseHttpsRedirection());

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapHealthChecks("/healthz");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
