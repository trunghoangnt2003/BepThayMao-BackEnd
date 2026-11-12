using System.Security.Claims;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;

namespace Bepthaymao.Api.Security;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser, IDataScope
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    // Ngoài yêu cầu HTTP (seed gọi từ tiến trình, tác vụ nền) không có HttpContext: đó là phạm vi hệ thống tin cậy.
    // Mọi yêu cầu HTTP luôn có HttpContext nên chỉ thấy dữ liệu theo claim owner_id/branch_id đã kiểm chữ ký.
    public bool IsSystem => accessor.HttpContext is null;

    public Guid? UserId => ReadGuid("sub");
    public Guid? OwnerId => ReadGuid("owner_id");
    public Guid? BranchId => ReadGuid("branch_id");
    public UserRole? Role => Enum.TryParse(Principal?.FindFirstValue("role"), out UserRole role) ? role : null;

    private Guid? ReadGuid(string claim) => Guid.TryParse(Principal?.FindFirstValue(claim), out Guid value) ? value : null;
}
