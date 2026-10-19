# Многоэтапная сборка: в первом образе (SDK) собираем, во второй (только runtime) копируем результат.
# Итоговый образ меньше и не содержит исходников и инструментов сборки.

# ---------- Этап 1: сборка ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только .csproj и restore: этот слой кешируется, пока не меняются зависимости.
COPY NewsPortal/NewsPortal.csproj NewsPortal/
RUN dotnet restore NewsPortal/NewsPortal.csproj

COPY NewsPortal/ NewsPortal/
RUN dotnet publish NewsPortal/NewsPortal.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

# ---------- Этап 2: запуск ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Production: страница ошибки без подробностей, HSTS, user-secrets не читаются.
ENV ASPNETCORE_ENVIRONMENT=Production
# Порт по умолчанию. Render передаёт свой порт через переменную PORT, приложение её учитывает.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Запуск не от root: пользователь app встроен в официальный образ .NET.
USER $APP_UID

# Секретов здесь нет: строка подключения передаётся при запуске
# через переменную окружения ConnectionStrings__DefaultConnection.
ENTRYPOINT ["dotnet", "NewsPortal.dll"]
