using System.Security.Claims;
using System.Text;
using Bepthaymao.Application.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bepthaymao.Api.Security;

internal sealed class JwtAccessTokenIssuer(IConfiguration configuration, TimeProvider clock) : IAccessTokenIssuer
{
    public (string Token, int ExpiresInSeconds) Issue(AuthenticatedUser user)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        int expiresIn = configuration.GetValue("Jwt:AccessTokenMinutes", 15) * 60;
        List<Claim> claims = [new("sub", user.Id.ToString()), new("name", user.UserName), new("role", user.Role.ToString())];
        if (user.OwnerId is { } ownerId) claims.Add(new("owner_id", ownerId.ToString()));
        if (user.BranchId is { } branchId) claims.Add(new("branch_id", branchId.ToString()));
        SecurityTokenDescriptor descriptor = new()
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = configuration["Jwt:Issuer"],
            Audience = configuration["Jwt:Audience"],
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(expiresIn),
            SigningCredentials = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"]!)), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresIn);
    }
}
