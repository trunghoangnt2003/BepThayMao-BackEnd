namespace Bepthaymao.Application.Auth;

public sealed record AuthenticatedUser(Guid Id, string UserName, UserRole Role, Guid? OwnerId, Guid? BranchId);
