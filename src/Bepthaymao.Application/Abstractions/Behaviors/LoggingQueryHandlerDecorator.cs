using System.Diagnostics;
using Bepthaymao.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal sealed class LoggingQueryHandlerDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner, ILogger<LoggingQueryHandlerDecorator<TQuery, TResponse>> logger)
    : IQueryHandler<TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    private static readonly Action<ILogger, string, Exception?> _handling = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(1003, "HandlingQuery"), "Handling {RequestType}");
    private static readonly Action<ILogger, string, double, Exception?> _handled = LoggerMessage.Define<string, double>(
        LogLevel.Information, new EventId(1004, "HandledQuery"), "Handled {RequestType} in {ElapsedMs} ms");

    public async Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        _handling(logger, typeof(TQuery).Name, null);
        TResponse result = await inner.Handle(query, cancellationToken).ConfigureAwait(false);
        _handled(logger, typeof(TQuery).Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds, null);
        return result;
    }
}
