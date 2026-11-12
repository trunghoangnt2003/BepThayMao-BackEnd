namespace Bepthaymao.Api.Security;

/// <summary>
/// Cấp quyền hệ thống tường minh cho một DI scope chạy ngoài yêu cầu HTTP (tác vụ nền, kiểm thử).
/// Mặc định không cấp: thiếu HttpContext không còn tự thành quyền hệ thống (fail-closed).
/// </summary>
internal sealed class SystemScopeGrant
{
    public bool Granted { get; set; }
}

internal static class SystemScopeExtensions
{
    /// <summary>Tạo DI scope ngoài yêu cầu HTTP có quyền hệ thống (thấy dữ liệu mọi cơ sở).</summary>
    public static IServiceScope CreateSystemScope(this IServiceProvider services)
    {
        IServiceScope scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SystemScopeGrant>().Granted = true;
        return scope;
    }
}
