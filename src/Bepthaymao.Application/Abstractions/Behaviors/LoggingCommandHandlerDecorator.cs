using System.Diagnostics;
using Bepthaymao.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal sealed class LoggingCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, ILogger<LoggingCommandHandlerDecorator<TCommand, TResponse>> logger)
    : ICommandHandler<TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    private static readonly Action<ILogger, string, Exception?> _handling = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(1001, "HandlingCommand"), "Handling {RequestType}");
    private static readonly Action<ILogger, string, double, Exception?> _handled = LoggerMessage.Define<string, double>(
        LogLevel.Information, new EventId(1002, "HandledCommand"), "Handled {RequestType} in {ElapsedMs} ms");

    public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        _handling(logger, typeof(TCommand).Name, null);
        TResponse result = await inner.Handle(command, cancellationToken).ConfigureAwait(false);
        _handled(logger, typeof(TCommand).Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds, null);
        return result;
    }
}
