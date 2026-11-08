using Bepthaymao.Application.Abstractions.Messaging;
using FluentValidation;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal sealed class ValidationQueryHandlerDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner, IEnumerable<IValidator<TQuery>> validators)
    : IQueryHandler<TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    public async Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken)
    {
        await RequestValidation.Validate(query, validators, cancellationToken).ConfigureAwait(false);
        return await inner.Handle(query, cancellationToken).ConfigureAwait(false);
    }
}
