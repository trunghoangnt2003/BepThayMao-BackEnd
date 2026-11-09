using Bepthaymao.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal sealed class LoggingCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    ILogger<LoggingCommandHandlerDecorator<TCommand, TResponse>> logger)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
    {
        string requestType = typeof(TCommand).Name;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        CqrsLogging.Handling(logger, requestType);
        try
        {
            return await inner.Handle(command, cancellationToken);
        }
        finally
        {
            CqrsLogging.Handled(logger, requestType, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}
