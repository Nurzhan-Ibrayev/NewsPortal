// Показывает даты (хранятся в UTC) в часовом поясе читателя.
document.querySelectorAll("time[data-local-time]").forEach(function (el) {
    var date = new Date(el.getAttribute("datetime"));
    if (!isNaN(date)) {
        el.textContent = date.toLocaleString("ru-RU", {
            day: "2-digit", month: "2-digit", year: "numeric",
            hour: "2-digit", minute: "2-digit"
        });
    }
});

// Подтверждение для форм с атрибутом data-confirm (например, блокировка пользователя).
// Встроенный onsubmit не используем: его запрещает Content-Security-Policy.
document.querySelectorAll("form[data-confirm]").forEach(function (form) {
    form.addEventListener("submit", function (event) {
        if (!window.confirm(form.getAttribute("data-confirm"))) {
            event.preventDefault();
        }
    });
});
