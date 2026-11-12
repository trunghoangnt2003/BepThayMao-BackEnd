namespace Bepthaymao.Application.Auth;

public sealed record TokenPair(string AccessToken, int ExpiresIn, UserRole Role, string Identifier);
