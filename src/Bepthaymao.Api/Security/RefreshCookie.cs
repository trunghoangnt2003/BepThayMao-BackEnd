using Bepthaymao.Application.Auth;

namespace Bepthaymao.Api.Security;

internal static class RefreshCookie
{
    public const string Name = "refresh_token";
    public const string Path = "/api/v1/auth";

    public static void Append(HttpResponse response, IssuedRefreshToken token) => response.Cookies.Append(Name, token.Token,
        Options(response.HttpContext, token.ExpiresAt));

    public static void Delete(HttpResponse response) => response.Cookies.Delete(Name, Options(response.HttpContext, null));

    private static CookieOptions Options(HttpContext context, DateTimeOffset? expiresAt) => new()
    {
        Path = Path,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Auth:RefreshCookie:Secure", true),
        Expires = expiresAt,
    };
}
