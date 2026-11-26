using Bepthaymao.Infrastructure.Messaging;
using Bepthaymao.Infrastructure.Notifications;
using RabbitMQ.Client;

namespace Bepthaymao.Worker;

internal sealed partial class NotifierWorker(
    IServiceScopeFactory scopes,
    RabbitBroker broker,
    ILogger<NotifierWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using IChannel channel = await broker.CreateConfirmChannelAsync(stoppingToken);
                for (int i = 0; i < 20 && !stoppingToken.IsCancellationRequested; i++)
                {
                    using IServiceScope scope = scopes.CreateScope();
                    NotifierConsumer consumer = scope.ServiceProvider.GetRequiredService<NotifierConsumer>();
                    if (!await consumer.ConsumeOneAsync(channel, stoppingToken)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // Không ghi exception broker/DB: có thể chứa URI credential hoặc BotToken.
                LogNotifierBatchFailure(logger, exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    [LoggerMessage(LogLevel.Error, "Notifier batch failed ({FailureType}); pending messages will be retried")]
    private static partial void LogNotifierBatchFailure(ILogger logger, string failureType);
}
