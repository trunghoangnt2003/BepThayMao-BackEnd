using System.Security.Claims;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;

namespace Bepthaymao.Api.Security;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => ReadGuid("sub");
    public Guid? OwnerId => ReadGuid("owner_id");
    public Guid? BranchId => ReadGuid("branch_id");
    public UserRole? Role => Enum.TryParse(Principal?.FindFirstValue("role"), out UserRole role) ? role : null;

    private Guid? ReadGuid(string claim) => Guid.TryParse(Principal?.FindFirstValue(claim), out Guid value) ? value : null;
}
