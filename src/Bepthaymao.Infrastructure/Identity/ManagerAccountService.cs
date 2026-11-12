using Bepthaymao.Application.Auth;
using Microsoft.AspNetCore.Identity;

namespace Bepthaymao.Infrastructure.Identity;

internal sealed class ManagerAccountService(UserManager<AppUser> users, TimeProvider clock) : IManagerAccountService
{
    public async Task<string> CreateManagerAsync(Guid branchId, string identifier, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppUser user = new() { Id = Guid.CreateVersion7(), UserName = identifier, BranchId = branchId, CreatedAt = clock.GetUtcNow() };
        RequireSuccess(await users.CreateAsync(user, password));
        RequireSuccess(await users.AddToRoleAsync(user, nameof(UserRole.BranchManager)));
        return identifier;
    }

    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Không thể tạo tài khoản quản lý: " + string.Join(", ", result.Errors.Select(error => error.Code)));
    }
}
