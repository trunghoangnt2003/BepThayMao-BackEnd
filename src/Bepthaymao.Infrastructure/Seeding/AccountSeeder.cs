using Bepthaymao.Domain.Owners;
using Bepthaymao.Infrastructure.Identity;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Seeding;

public sealed class AccountSeeder(UserManager<AppUser> users, AppDbContext db, TimeProvider clock)
{
    public async Task<(Guid OwnerId, bool Created)> EnsureOwnerAsync(string userName, string name, string password, CancellationToken cancellationToken)
    {
        AppUser? existing = await users.FindByNameAsync(userName);
        if (existing is not null)
        {
            if (existing.OwnerId is not { } ownerId || !await users.IsInRoleAsync(existing, "Owner"))
                throw new InvalidOperationException("Tên đăng nhập không thuộc tài khoản Chủ sở hữu.");
            return (ownerId, false);
        }
        Owner owner = Owner.Create(Guid.CreateVersion7(), name, clock.GetUtcNow());
        db.Owners.Add(owner);
        await db.SaveChangesAsync(cancellationToken);
        AppUser user = new() { Id = Guid.CreateVersion7(), UserName = userName, OwnerId = owner.Id, CreatedAt = clock.GetUtcNow() };
        RequireSuccess(await users.CreateAsync(user, password));
        RequireSuccess(await users.AddToRoleAsync(user, "Owner"));
        return (owner.Id, true);
    }

    public async Task<bool> EnsureManagerAsync(Guid branchId, string userName, string password, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(user => user.BranchId == branchId, cancellationToken)) return false;
        if (await users.FindByNameAsync(userName) is not null)
            throw new InvalidOperationException("Tên đăng nhập quản lý đã được sử dụng.");
        AppUser user = new() { Id = Guid.CreateVersion7(), UserName = userName, BranchId = branchId, CreatedAt = clock.GetUtcNow() };
        RequireSuccess(await users.CreateAsync(user, password));
        RequireSuccess(await users.AddToRoleAsync(user, "BranchManager"));
        return true;
    }

    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Không thể tạo tài khoản: " + string.Join(", ", result.Errors.Select(error => error.Code)));
    }
}
