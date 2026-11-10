using Bepthaymao.Application.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Bepthaymao.Infrastructure.Identity;

internal sealed class IdentityUserAuthenticator(UserManager<AppUser> users, IPasswordHasher<AppUser> hasher)
    : IUserAuthenticator
{
    // One real Identity hash uses the same configured work factor for unknown accounts.
    private static readonly string _dummyHash = new PasswordHasher<AppUser>(Options.Create(new PasswordHasherOptions()))
        .HashPassword(new AppUser(), "timing-comparison-only");

    public async Task<AuthenticatedUser?> AuthenticateAsync(string identifier, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppUser? user = await users.FindByNameAsync(identifier);
        if (user is null)
        {
            _ = hasher.VerifyHashedPassword(new AppUser(), _dummyHash, password);
            return null;
        }
        if (!await users.CheckPasswordAsync(user, password)) return null;
        IList<string> roles = await users.GetRolesAsync(user);
        if (roles.Count != 1 || !Enum.TryParse(roles[0], out UserRole role)) return null;
        return new(user.Id, user.UserName!, role, user.OwnerId, user.BranchId);
    }
}
