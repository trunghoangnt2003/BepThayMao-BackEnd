using Bepthaymao.Application.Auth;

namespace Bepthaymao.Application.Abstractions.Security;

/// <summary>Chủ thể của yêu cầu hiện tại, đọc từ claim JWT đã kiểm chữ ký.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    UserRole? Role { get; }
    Guid? OwnerId { get; }
    Guid? BranchId { get; }
}
