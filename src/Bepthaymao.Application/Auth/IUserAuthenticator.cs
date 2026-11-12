namespace Bepthaymao.Application.Auth;

public interface IUserAuthenticator
{
    Task<AuthenticatedUser?> AuthenticateAsync(string identifier, string password, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
}
