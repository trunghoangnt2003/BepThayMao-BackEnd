using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal sealed class UnitOfWorkCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IUnitOfWork unitOfWork)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
    {
        TResponse response = await inner.Handle(command, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
