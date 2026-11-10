using System.Security.Cryptography;
using System.Text;
using Bepthaymao.Application.Auth;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

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

    public async Task<RefreshRotation> RotateAsync(string rawToken, CancellationToken cancellationToken)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Lock the predecessor until its successor is committed, including across API replicas.
        RefreshToken? previous = await db.RefreshTokens
            .FromSql($"SELECT * FROM refresh_tokens WHERE token_hash = {hash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (previous is null || previous.ExpiresAt <= clock.GetUtcNow()) return new RefreshRotation.Invalid();
        if (previous.RevokedAt is not null)
            return previous.ReplacedById is not null ? new RefreshRotation.Reused(previous.UserId) : new RefreshRotation.Invalid();
        IssuedRefreshToken next = await IssueAsync(previous.UserId, previous.FamilyId, cancellationToken);
        RefreshToken successor = db.RefreshTokens.Local.Single(value => value.TokenHash ==
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(next.Token))));
        previous.RevokedAt = clock.GetUtcNow();
        previous.ReplacedById = successor.Id;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefreshRotation.Rotated(previous.UserId, next);
    }
}
