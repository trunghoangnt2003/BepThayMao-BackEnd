using Bepthaymao.Infrastructure.Orders;

namespace Bepthaymao.Worker;

/// <summary>Vỏ mỏng quanh <see cref="OrderExpirySweeper"/>: logic quét nằm ở Infrastructure để test được bằng thời gian giả.</summary>
internal sealed class OrderExpiryWorker(OrderExpirySweeper sweeper) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => sweeper.RunAsync(stoppingToken);
}
