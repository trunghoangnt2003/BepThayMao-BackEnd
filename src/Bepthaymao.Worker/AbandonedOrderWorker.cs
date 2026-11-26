using Bepthaymao.Infrastructure.Orders;

namespace Bepthaymao.Worker;

internal sealed class AbandonedOrderWorker(AbandonedOrderSweeper sweeper) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => sweeper.RunAsync(stoppingToken);
}
