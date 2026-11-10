using System.Security.Cryptography;
using System.Text;
using Bepthaymao.Application.Auth;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace Bepthaymao.Infrastructure.Identity;

internal sealed class RefreshTokenService(AppDbContext db, TimeProvider clock, IConfiguration configuration) : IRefreshTokenService
{
    public Task<IssuedRefreshToken> IssueAsync(Guid userId, Guid? familyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset expiresAt = now.AddDays(configuration.GetValue("Jwt:RefreshTokenDays", 7));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FamilyId = familyId ?? Guid.CreateVersion7(),
            TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        return Task.FromResult(new IssuedRefreshToken(token, expiresAt));
    }
}
