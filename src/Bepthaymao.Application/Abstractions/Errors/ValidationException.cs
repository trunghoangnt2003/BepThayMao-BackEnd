namespace Bepthaymao.Application.Abstractions.Errors;

public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Request validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
