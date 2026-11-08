using Bepthaymao.Application.Abstractions.Messaging;
using FluentValidation;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal sealed class ValidationCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
    {
        await RequestValidation.Validate(command, validators, cancellationToken).ConfigureAwait(false);
        return await inner.Handle(command, cancellationToken).ConfigureAwait(false);
    }
}
