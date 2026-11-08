using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using ValidationException = Bepthaymao.Application.Abstractions.Errors.ValidationException;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal static class RequestValidation
{
    public static async Task Validate<TRequest>(TRequest request, IEnumerable<IValidator<TRequest>> validators,
        CancellationToken cancellationToken)
    {
        List<ValidationFailure> failures = [];
        foreach (IValidator<TRequest> validator in validators)
        {
            ValidationResult result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            Dictionary<string, string[]> errors = failures.GroupBy(
                failure => string.Join('.', failure.PropertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)),
                StringComparer.Ordinal).ToDictionary(group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
            throw new ValidationException(errors);
        }
    }
}
