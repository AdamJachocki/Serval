using Microsoft.AspNetCore.Http;

namespace Serval.Web;

public static class ServalSessionCookie
{
    public const string Name = "__Host-ServalSession";

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var token) ? token : null;

    public static void Set(HttpResponse response, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        response.Cookies.Append(Name, token, new CookieOptions
        {
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });
    }

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name,
        new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
}
