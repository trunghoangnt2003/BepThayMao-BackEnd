using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;

namespace Bepthaymao.Worker;

/// <summary>Worker không có phiên người dùng và không được quyền xác nhận thủ công.</summary>
internal sealed class WorkerCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public UserRole? Role => null;
    public Guid? OwnerId => null;
    public Guid? BranchId => null;
}
