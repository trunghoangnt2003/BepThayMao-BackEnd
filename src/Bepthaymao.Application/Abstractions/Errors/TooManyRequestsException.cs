namespace Bepthaymao.Application.Abstractions.Errors;

public sealed class TooManyRequestsException(int retryAfterSeconds) : Exception
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
