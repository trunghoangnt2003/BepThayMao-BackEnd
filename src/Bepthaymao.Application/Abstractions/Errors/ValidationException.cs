using System.Collections.ObjectModel;

namespace Bepthaymao.Application.Abstractions.Errors;

public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(
            new Dictionary<string, string[]>(errors, StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
