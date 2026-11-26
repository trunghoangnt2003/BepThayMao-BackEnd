using System.Text.Json;
using Bepthaymao.Domain.Events;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bepthaymao.Infrastructure.Orders;

/// <summary>
/// Vòng quét đơn đã thanh toán nhưng chưa được nhận quá ngưỡng <c>abandoned_threshold_minutes</c> của cơ sở.
/// Mỗi đơn chỉ cảnh báo đúng một lần bằng <c>abandoned_alerted_at</c> và phát <c>order.abandoned</c> vào outbox cùng transaction.
/// </summary>
public sealed partial class AbandonedOrderSweeper(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AbandonedOrderSweeper> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    public const int BatchSize = 100;
    public const int MaxBatchesPerSweep = 20;
    private static readonly JsonSerializerOptions _json = JsonSerializerOptions.Web;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SweepOnceAsync(stoppingToken);
            try { await Task.Delay(Interval, clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        int total = 0;
        try
        {
            for (int batch = 0; batch < MaxBatchesPerSweep; batch++)
            {
                using IServiceScope scope = scopes.CreateScope();
                AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                int alerted = await AlertBatchAsync(db, cancellationToken);
                total += alerted;
                if (alerted < BatchSize) break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return total; }
        catch (Exception exception)
        {
            LogSweepFailed(logger, exception);
        }
        return total;
    }

    public async Task<int> AlertBatchAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        DateTimeOffset now = clock.GetUtcNow();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);

        List<Order> candidates = await db.Orders.FromSql($"""
            SELECT o.*
            FROM orders o
            JOIN branches b ON b.id = o.branch_id
            WHERE o.status = 'Paid'
              AND o.abandoned_alerted_at IS NULL
              AND o.paid_recorded_at IS NOT NULL
              AND o.paid_recorded_at <= {now} - (b.abandoned_threshold_minutes * INTERVAL '1 minute')
            ORDER BY o.paid_recorded_at
            LIMIT {BatchSize}
            FOR UPDATE OF o SKIP LOCKED
            """).ToListAsync(cancellationToken);

        int count = 0;
        foreach (Order order in candidates)
        {
            if (!order.MarkAbandonedAlerted(now) || order.PaidRecordedAt is null) continue;
            int waitedMinutes = Math.Max(1, (int)Math.Floor((now - order.PaidRecordedAt.Value).TotalMinutes));
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                Type = "order.abandoned",
                Payload = JsonSerializer.Serialize(new
                {
                    orderId = order.Id,
                    branchId = order.BranchId,
                    waitedMinutes
                }, _json),
                OccurredAt = now
            });
            count++;
        }

        if (count > 0) await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return count;
    }

    [LoggerMessage(LogLevel.Error, "Abandoned order sweep failed; pending alerts will retry on next tick")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
