using Bepthaymao.Infrastructure.Messaging;

namespace Bepthaymao.Worker;

internal sealed partial class PaymentWorker(IServiceScopeFactory scopes, ILogger<PaymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope messagingScope = scopes.CreateScope();
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await messagingScope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                MessagingMetrics.DispatchFailed();
                // Không ghi exception broker/DB: có thể chứa URI credential hoặc payload.
                LogBatchFailure(logger, exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    [LoggerMessage(LogLevel.Error, "Messaging batch failed ({FailureType}); pending records will be retried")]
    private static partial void LogBatchFailure(ILogger logger, string failureType);
}
