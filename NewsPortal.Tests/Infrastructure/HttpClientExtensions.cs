using System.Net;
using System.Text.RegularExpressions;

namespace NewsPortal.Tests.Infrastructure;

public static partial class HttpClientExtensions
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();

    // Открывает страницу с формой, берёт из неё antiforgery-токен и отправляет POST —
    // так же, как это делает браузер. Для вошедшего пользователя подойдёт любая страница:
    // в меню есть форма «Выйти» с токеном.
    public static async Task<HttpResponseMessage> SubmitFormAsync(
        this HttpClient client,
        string pageWithFormUrl,
        string postUrl,
        Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(pageWithFormUrl);
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, $"На странице {pageWithFormUrl} не найден antiforgery-токен.");

        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
        };

        return await client.PostAsync(postUrl, new FormUrlEncodedContent(form));
    }

    public static Task<HttpResponseMessage> SubmitFormAsync(this HttpClient client, string postUrl, Dictionary<string, string>? fields = null) =>
        client.SubmitFormAsync("/", postUrl, fields ?? []);

    public static void AssertRedirectsTo(this HttpResponseMessage response, string pathStart)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        Assert.StartsWith(pathStart, new Uri(new Uri("http://localhost"), location).PathAndQuery);
    }
}
