using System.Security.Cryptography;
using System.Text;
using Bepthaymao.Application.Auth;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
        Guid? userId = await db.RefreshTokens.Where(value => value.TokenHash == hash)
            .Select(value => (Guid?)value.UserId).SingleOrDefaultAsync(cancellationToken);
        if (userId is null) return new RefreshRotation.Invalid();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockUser(userId.Value, cancellationToken);
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

    public async Task RevokeForTokenAsync(string rawToken, CancellationToken cancellationToken)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        Guid? userId = await db.RefreshTokens.Where(value => value.TokenHash == hash)
            .Select(value => (Guid?)value.UserId).SingleOrDefaultAsync(cancellationToken);
        if (userId is not null) await RevokeAllForUserAsync(userId.Value, cancellationToken);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Handler đã mở transaction (đặt lại mật khẩu + thu hồi phiên) thì tham gia nó, không mở transaction lồng.
        await using IDbContextTransaction? own = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        await LockUser(userId, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        await db.RefreshTokens.Where(value => value.UserId == userId && value.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.RevokedAt, now), cancellationToken);
        if (own is not null) await own.CommitAsync(cancellationToken);
    }

    private async Task LockUser(Guid userId, CancellationToken cancellationToken)
    {
        // Serialize rotations and all-session revocations for this user across replicas.
        _ = await db.Users.FromSql($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }
}
