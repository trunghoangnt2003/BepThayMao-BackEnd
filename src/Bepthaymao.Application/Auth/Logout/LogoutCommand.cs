using Bepthaymao.Application.Abstractions.Messaging;

namespace Bepthaymao.Application.Auth.Logout;

public sealed record LogoutCommand(string? RefreshToken) : ICommand<bool>;

internal sealed class LogoutCommandHandler(IRefreshTokenService refreshTokens) : ICommandHandler<LogoutCommand, bool>
{
    public async Task<bool> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
            await refreshTokens.RevokeForTokenAsync(command.RefreshToken, cancellationToken);
        return true;
    }
}
