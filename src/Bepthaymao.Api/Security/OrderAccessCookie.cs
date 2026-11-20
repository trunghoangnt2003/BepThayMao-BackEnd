using System.Text.RegularExpressions;
using Bepthaymao.Application.Orders;

namespace Bepthaymao.Api.Security;

/// <summary>Cookie gắn đơn công khai (SEC-01): giá trị là tối đa 5 token base64url 43 ký tự nối bằng dấu `.`,
/// không chứa số điện thoại, tên hay mã đơn. Mẫu theo <see cref="RefreshCookie"/>.</summary>
internal static partial class OrderAccessCookie
{
    public const string Name = "btm_don";
    public const int MaxTokens = 5;

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex TokenShape();

    /// <summary>Token hợp lệ (đúng hình dạng) đang có trong request, mới nhất trước.</summary>
    public static IReadOnlyList<string> Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(Name, out string? raw) || string.IsNullOrEmpty(raw)) return [];
        return [.. raw.Split('.').Where(token => TokenShape().IsMatch(token)).Take(MaxTokens)];
    }

    /// <summary>Đặt cookie mới nhất lên đầu, giữ tối đa <see cref="MaxTokens"/> token, bỏ trùng.</summary>
    public static void Append(HttpResponse response, IReadOnlyList<string> existing, IssuedOrderAccess access)
    {
        string[] tokens = [.. new[] { access.Token }.Concat(existing.Where(token => token != access.Token)).Take(MaxTokens)];
        response.Cookies.Append(Name, string.Join('.', tokens), Options(response.HttpContext, access.ExpiresAt));
    }

    private static CookieOptions Options(HttpContext context, DateTimeOffset expiresAt) => new()
    {
        Path = "/",
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Orders:AccessCookie:Secure", true),
        Expires = expiresAt,
    };
}
