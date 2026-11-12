using System.Security.Claims;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;

namespace Bepthaymao.Api.Security;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, SystemScopeGrant systemGrant) : ICurrentUser, IDataScope
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    // Fail-closed: chỉ scope ngoài yêu cầu HTTP được cấp tường minh (CreateSystemScope) mới là phạm vi hệ thống.
    // Thiếu HttpContext mà không được cấp thì không thấy dữ liệu cơ sở nào; yêu cầu HTTP luôn theo claim đã kiểm chữ ký.
    public bool IsSystem => accessor.HttpContext is null && systemGrant.Granted;

    public Guid? UserId => ReadGuid("sub");
    public Guid? OwnerId => ReadGuid("owner_id");
    public Guid? BranchId => ReadGuid("branch_id");
    public UserRole? Role => Enum.TryParse(Principal?.FindFirstValue("role"), out UserRole role) ? role : null;

    private Guid? ReadGuid(string claim) => Guid.TryParse(Principal?.FindFirstValue(claim), out Guid value) ? value : null;
}
