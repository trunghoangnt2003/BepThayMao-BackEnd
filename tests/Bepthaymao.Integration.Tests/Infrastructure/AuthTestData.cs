using Bepthaymao.Application.Auth;
using Bepthaymao.Domain.Owners;
using Bepthaymao.Infrastructure.Identity;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Infrastructure;

internal static class AuthTestData
{
    public static async Task<AppUser> CreateUserAsync(IServiceProvider services, string userName, string password,
        UserRole role, Guid? ownerId = null, Guid? branchId = null)
    {
        AppDbContext db = services.GetRequiredService<AppDbContext>();
        if (ownerId is { } id)
        {
            db.Owners.Add(Owner.Create(id, "Test owner", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        UserManager<AppUser> users = services.GetRequiredService<UserManager<AppUser>>();
        AppUser user = new() { Id = Guid.CreateVersion7(), UserName = userName, OwnerId = ownerId, BranchId = branchId, CreatedAt = DateTimeOffset.UtcNow };
        IdentityResult created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join(";", created.Errors.Select(error => error.Description)));
        IdentityResult assigned = await users.AddToRoleAsync(user, role.ToString());
        Assert.True(assigned.Succeeded, string.Join(";", assigned.Errors.Select(error => error.Description)));
        return user;
    }
}
