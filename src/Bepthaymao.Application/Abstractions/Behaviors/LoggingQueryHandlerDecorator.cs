using Bepthaymao.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal sealed class LoggingQueryHandlerDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    ILogger<LoggingQueryHandlerDecorator<TQuery, TResponse>> logger)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken)
    {
        string requestType = typeof(TQuery).Name;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        CqrsLogging.Handling(logger, requestType);
        try
        {
            return await inner.Handle(query, cancellationToken);
        }
        finally
        {
            CqrsLogging.Handled(logger, requestType, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}
