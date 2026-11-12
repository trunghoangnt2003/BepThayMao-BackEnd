using System.Text.Json;
using AppValidationException = Bepthaymao.Application.Abstractions.Errors.ValidationException;
using Bepthaymao.Application.Abstractions.Messaging;
using FluentValidation;
using FluentValidation.Results;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal sealed class ValidationQueryHandlerDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    IEnumerable<IValidator<TQuery>> validators)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken)
    {
        ValidationContext<TQuery> context = new(query);
        ValidationFailure[] failures = (await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToArray();

        if (failures.Length > 0)
        {
            Dictionary<string, string[]> errors = failures
                .GroupBy(failure => ToCamelCasePath(failure.PropertyName), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);
            throw new AppValidationException(errors);
        }

        return await inner.Handle(query, cancellationToken);
    }

    private static string ToCamelCasePath(string propertyPath)
    {
        return string.Join('.', propertyPath.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
    }
}
