namespace Bepthaymao.Application.Auth;

public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

public abstract record RefreshRotation
{
    public sealed record Rotated(Guid UserId, IssuedRefreshToken Next) : RefreshRotation;
    public sealed record Reused(Guid UserId) : RefreshRotation;
    public sealed record Invalid : RefreshRotation;
}

public interface IRefreshTokenService
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, Guid? familyId, CancellationToken cancellationToken);
    Task<RefreshRotation> RotateAsync(string rawToken, CancellationToken cancellationToken);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task RevokeForTokenAsync(string rawToken, CancellationToken cancellationToken);
}
