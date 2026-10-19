# NewsPortal

Портал новостей с модерацией. Авторы пишут статьи, модераторы их проверяют, а опубликованные новости видны всем посетителям без входа.

## Роли

| Роль | Возможности |
|---|---|
| **Admin** | Создаёт пользователей, редактирует их данные и роли, блокирует и разблокирует. Прав модерации не имеет. |
| **Author** | Создаёт статьи (они сразу уходят на модерацию), видит только свои статьи и их статусы, исправляет отклонённые и отправляет повторно. |
| **Moderator** | Видит очередь модерации, публикует или отклоняет статьи (с обязательной причиной). Текст статей не редактирует. |
| Гость | Читает опубликованные новости. |

Публичной регистрации нет: учётные записи создаёт администратор.

Жизненный цикл статьи:

```
создание → На модерации → Опубликована
                ↓    ↑
            Отклонена (автор исправляет и отправляет повторно)
```

## Технологии

- .NET 10, ASP.NET Core MVC, Razor Views, Bootstrap 5
- ASP.NET Core Identity (пользователи, роли, хеширование паролей)
- Entity Framework Core 10 + Npgsql
- PostgreSQL в [Neon](https://neon.tech)
- xUnit, `WebApplicationFactory`, EF Core InMemory (тесты)

## Структура

```
NewsPortal/                     ← корень репозитория
├── NewsPortal.slnx
├── dotnet-tools.json           ← локальный инструмент dotnet-ef
├── Dockerfile, .dockerignore, docker-compose.yml, .env.example
├── NewsPortal/                 ← веб-приложение
│   ├── Controllers/            ← Home (публичная часть), Account, MyArticles, Moderation, Users
│   ├── Data/                   ← ApplicationDbContext
│   ├── Migrations/             ← миграции EF Core
│   ├── Models/                 ← ApplicationUser, Article, ArticleStatus, Roles
│   ├── Services/               ← ArticleService (правила статусов), AdminSeeder
│   ├── ViewModels/             ← модели форм
│   ├── Views/
│   └── wwwroot/
└── NewsPortal.Tests/           ← автоматические тесты
```

## Требования

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Бесплатный аккаунт [Neon](https://neon.tech). Устанавливать PostgreSQL на компьютер не нужно.

## Локальный запуск

### 1. Клонировать и восстановить зависимости

```powershell
git clone https://github.com/Nurzhan-Ibrayev/NewsPortal.git
cd NewsPortal
dotnet tool restore      # устанавливает dotnet-ef из dotnet-tools.json
dotnet restore
dotnet build
```

### 2. Создать базу в Neon

1. Зарегистрируйтесь на <https://console.neon.tech> и создайте проект. База `neondb` создаётся автоматически.
2. Откройте **Connect** на панели проекта.
3. Выключите **Connection pooling**: для миграций нужен прямой адрес, без `-pooler` в имени хоста.
4. Скопируйте строку подключения. Neon показывает её в виде URI:

   ```
   postgresql://USER:PASSWORD@HOST/DBNAME?sslmode=require
   ```

   Npgsql ожидает формат «ключ=значение», поэтому переведите её так:

   ```
   Host=HOST;Database=DBNAME;Username=USER;Password=PASSWORD;SSL Mode=Require
   ```

### 3. Сохранить строку подключения (локально)

Строка подключения хранится в **user-secrets**. Это файл в профиле пользователя, вне репозитория, в Git он не попадает.

```powershell
cd NewsPortal
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require"
```

Пароль из этой команды останется в истории терминала. Чтобы этого избежать, впишите значение вручную в файл
`%APPDATA%\Microsoft\UserSecrets\fbecbb0d-5937-44bc-a7a7-44e1268bc6be\secrets.json`
(Linux/macOS: `~/.microsoft/usersecrets/fbecbb0d-5937-44bc-a7a7-44e1268bc6be/secrets.json`):

```json
{
  "ConnectionStrings:DefaultConnection": "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require"
}
```

Вместо user-secrets можно задать переменную окружения `ConnectionStrings__DefaultConnection`.

### 4. Применить миграции к Neon

Миграции применяются **отдельной командой**, при старте сайта они не запускаются.

```powershell
cd NewsPortal            # папка с NewsPortal.csproj
dotnet ef database update
```

Команда идемпотентна: уже применённые миграции пропускаются. Проверить состояние можно командой `dotnet ef migrations list`.

### 5. Создать первого администратора

Администратор создаётся один раз командой `seed-admin`. Email и пароль передаются через user-secrets или переменные окружения (`AdminSeed__Email`, `AdminSeed__Password`, `AdminSeed__DisplayName`), в коде их нет.

```powershell
dotnet user-secrets set "AdminSeed:Email" "admin@example.com"
dotnet user-secrets set "AdminSeed:DisplayName" "Администратор"
dotnet user-secrets set "AdminSeed:Password" "..."
dotnet run -- seed-admin
```

Требования к паролю: не менее 8 символов, есть цифра, строчная и заглавная буква.

Что делает команда:
- создаёт роли `Admin`, `Author`, `Moderator`, если их ещё нет;
- создаёт администратора, если пользователя с таким email ещё нет;
- при повторном запуске ничего не дублирует;
- существующего пользователя без роли Admin молча не повышает, а завершается с ошибкой;
- пароль не выводит в лог.

После успешного создания пароль можно удалить из user-secrets: `dotnet user-secrets remove "AdminSeed:Password"`.

### 6. Запустить сайт

```powershell
dotnet run
```

Откройте <http://localhost:5081>, войдите как администратор, затем в разделе «Пользователи» создайте авторов и модераторов.

## Запуск в Docker

`Dockerfile` в корне репозитория собирает приложение в два этапа: SDK → runtime. Итоговый образ:
- содержит только опубликованное приложение, без исходников и тестов;
- работает в режиме `Production`;
- запускается не от root.

Секретов в образе нет: строка подключения передаётся переменной окружения при запуске.

Через docker compose (поднимает только приложение, база остаётся в Neon):

```powershell
copy .env.example .env      # затем впишите в .env строку подключения Neon
docker compose up --build
```

Сайт откроется на <http://localhost:8080>. Файл `.env` находится в `.gitignore` и в образ не копируется (`.dockerignore`).

Без compose:

```powershell
docker build -t newsportal .
docker run --rm -p 8080:8080 -e "ConnectionStrings__DefaultConnection=Host=...;SSL Mode=Require" newsportal
```

Порт:
- по умолчанию приложение слушает `8080`;
- если задана переменная `PORT` (её передаёт, например, Render), используется этот порт;
- проверка работоспособности: `GET /healthz` → `Healthy`.

Миграции и создание администратора контейнер **не выполняет**. Они запускаются командами из шагов 4–5 с компьютера разработчика, напрямую в Neon.

## Развёртывание на Render

Render собирает образ из `Dockerfile` прямо из GitHub-репозитория. База остаётся в Neon.

### 1. Подготовить базу (с компьютера разработчика)

У Render на бесплатном тарифе нет консоли (Shell), поэтому миграции и создание администратора выполняются локально, напрямую в ту же базу Neon. Используйте шаги 3–5 из раздела «Локальный запуск». Если они уже выполнены, повторять не нужно: обе команды идемпотентны.

### 2. Создать Web Service

**Render Dashboard → New → Web Service** → подключите GitHub и выберите репозиторий `NewsPortal`.

| Настройка | Значение |
|---|---|
| Language / Runtime | **Docker** |
| Branch | `main` |
| Root Directory | пусто (Dockerfile в корне репозитория) |
| Dockerfile Path | `./Dockerfile` |
| Docker Build Context | `.` |
| Region | тот же регион, что у базы Neon (например, Frankfurt для AWS `eu-central-1`): меньше задержка запросов к БД |
| Instance Type | Free |
| Health Check Path (Advanced) | `/healthz` |
| Auto-Deploy | On Commit |

Build Command и Start Command для Docker не заполняются: их задают `Dockerfile` и `ENTRYPOINT`.

### 3. Переменные окружения (Environment)

| Ключ | Значение | Зачем |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `Host=...;Database=...;Username=...;Password=...;SSL Mode=Require` | строка подключения Neon (секрет, вводится только в панели Render) |
| `HTTPS_PORT` | `443` | включает редирект HTTP → HTTPS |

`PORT` задавать не нужно: Render передаёт его сам, и приложение слушает этот порт. `ASPNETCORE_ENVIRONMENT=Production` уже задан в `Dockerfile`.

### 4. Как приложение работает за прокси Render

Render принимает HTTPS и передаёт запрос в контейнер по HTTP с заголовками `X-Forwarded-Proto` и `X-Forwarded-For`. Приложение читает эти заголовки (`UseForwardedHeaders`), поэтому:
- HTTP-запросы перенаправляются на `https://`;
- в ответах по HTTPS есть заголовок HSTS;
- cookie входа и antiforgery получают флаг `Secure`.

Ключи Data Protection, которыми шифруются cookie входа и CSRF-токены, хранятся в Neon (таблица `DataProtectionKeys`). Поэтому после перезапуска или нового деплоя пользователей не разлогинивает.

### Ограничения бесплатного тарифа

На момент написания бесплатный сервис Render засыпает после периода простоя, и первый запрос после этого может занять до минуты. Бесплатная база Neon тоже приостанавливается без нагрузки. На случай кратких обрывов соединения приложение повторяет запросы (`EnableRetryOnFailure`). Актуальные лимиты смотрите в документации Render и Neon.

## Тесты

```powershell
dotnet test              # из корня репозитория
```

Тесты запускают приложение целиком в памяти (`WebApplicationFactory`) с отдельной базой EF InMemory на каждый тест. **Neon при этом не используется**, строка подключения не нужна.

Что проверяется:
- **гость:** видит только опубликованные статьи; закрытые страницы перенаправляют на вход; регистрации нет;
- **автор:**
  - статья создаётся со статусом «На модерации»;
  - подставленные в форму `Status` и `AuthorId` игнорируются;
  - чужие статьи недоступны по прямому URL (IDOR);
  - отклонённую статью можно исправить, остальные нельзя;
  - нет доступа к модерации и админке;
- **модератор:** публикация; отклонение только с причиной; отклонённую статью нельзя опубликовать напрямую; нет доступа к админке и редактированию;
- **администратор:**
  - создание пользователей и смена ролей;
  - несуществующую роль назначить нельзя;
  - блокировка завершает уже открытую сессию и запрещает вход;
  - нельзя заблокировать себя или снять с себя роль Admin;
  - прав модерации у администратора нет;
- **безопасность:** POST без CSRF-токена отклоняется; HTML в статьях экранируется; заголовки безопасности на месте; открытое перенаправление после входа невозможно; за HTTPS-прокси отправляется HSTS.

## Безопасность

- Права проверяются на сервере через `[Authorize(Roles = ...)]` на каждом контроллере. Скрытие ссылок в меню — только для удобства.
- Запросы автора всегда фильтруются по `AuthorId` текущего пользователя, поэтому чужой ID в URL даёт 404.
- Формы содержат только разрешённые поля. Статус, автора и модератора задаёт сервер.
- Все правила смены статуса собраны в одном месте — `Services/ArticleService.cs`.
- CSRF: antiforgery-токен проверяется у всех POST-запросов (глобальный фильтр).
- XSS: Razor экранирует вывод, `Html.Raw` не используется. Content-Security-Policy запрещает встроенные скрипты.
- Пароли хранятся только в виде хеша (Identity). После 5 неудачных попыток вход временно блокируется.
- Блокировка пользователя меняет его SecurityStamp, поэтому уже выданные cookie перестают действовать на следующем запросе.
- Секреты (строка подключения, пароль администратора) хранятся только в user-secrets или переменных окружения.
- Заголовки `Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`. В production за HTTPS-прокси дополнительно включаются HSTS и cookie с флагом `Secure`.
- В production пользователю показывается страница ошибки без технических подробностей.
