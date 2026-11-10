using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Auth.Login;

namespace Bepthaymao.Application.Auth.Refresh;

public sealed record RefreshSessionCommand(string? RefreshToken) : ICommand<LoginResult>;

internal sealed class RefreshSessionCommandHandler(IRefreshTokenService refreshTokens,
    IUserAuthenticator authenticator, IAccessTokenIssuer accessTokens) : ICommandHandler<RefreshSessionCommand, LoginResult>
{
    public async Task<LoginResult> Handle(RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken)) throw new AuthenticationFailedException();
        RefreshRotation rotation = await refreshTokens.RotateAsync(command.RefreshToken, cancellationToken);
        if (rotation is not RefreshRotation.Rotated rotated) throw new AuthenticationFailedException();
        AuthenticatedUser user = await authenticator.FindByIdAsync(rotated.UserId, cancellationToken)
            ?? throw new AuthenticationFailedException();
        (string token, int expiresIn) = accessTokens.Issue(user);
        return new(new(token, expiresIn, user.Role, user.UserName), rotated.Next);
    }
}
