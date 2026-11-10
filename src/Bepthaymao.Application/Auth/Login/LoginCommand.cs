using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using FluentValidation;

namespace Bepthaymao.Application.Auth.Login;

public sealed record LoginCommand(string Identifier, string Password) : ICommand<LoginResult>;
public sealed record LoginResult(TokenPair Tokens, IssuedRefreshToken Refresh);

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Identifier).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Password).NotEmpty().MinimumLength(10).MaximumLength(128);
    }
}

internal sealed class LoginCommandHandler(IUserAuthenticator authenticator, IAccessTokenIssuer accessTokens,
    IRefreshTokenService refreshTokens) : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        AuthenticatedUser user = await authenticator.AuthenticateAsync(command.Identifier, command.Password, cancellationToken)
            ?? throw new AuthenticationFailedException();
        (string token, int expiresIn) = accessTokens.Issue(user);
        IssuedRefreshToken refresh = await refreshTokens.IssueAsync(user.Id, null, cancellationToken);
        return new(new(token, expiresIn, user.Role, user.UserName), refresh);
    }
}
