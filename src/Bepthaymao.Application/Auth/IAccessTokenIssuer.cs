namespace Bepthaymao.Application.Auth;

public interface IAccessTokenIssuer
{
    (string Token, int ExpiresInSeconds) Issue(AuthenticatedUser user);
}
