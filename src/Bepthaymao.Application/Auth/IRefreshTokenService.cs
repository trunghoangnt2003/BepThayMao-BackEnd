namespace Bepthaymao.Application.Auth;

public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

public interface IRefreshTokenService
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, Guid? familyId, CancellationToken cancellationToken);
}
