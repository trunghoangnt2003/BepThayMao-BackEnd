using Bepthaymao.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bepthaymao.Infrastructure.Orders;

/// <summary>
/// Vòng quét hết hạn đơn của Worker: mỗi 15 giây, mỗi batch một DI scope riêng (DbContext mới, transaction ngắn).
/// Lỗi một batch được ghi log kèm mã tương quan và đếm vào số đo, rồi lần quét sau thử lại; job không chết im lặng.
/// Chờ bằng <see cref="TimeProvider"/> nên test điều khiển được thời gian mà không dùng Task.Delay thật.
/// </summary>
public sealed partial class OrderExpirySweeper(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OrderExpirySweeper> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    /// <summary>Trần số batch mỗi lần quét (20 x 100 đơn), để một đợt tồn đọng lớn không giữ job vô hạn.</summary>
    public const int MaxBatchesPerSweep = 20;

    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;
    private long _completedSweeps;
    private long _failedSweeps;

    /// <summary>Số lần quét đã chạy tới cuối (kể cả lần lỗi); tăng đều khi job còn sống, dùng làm dấu hiệu sức khỏe.</summary>
    public long CompletedSweeps => Interlocked.Read(ref _completedSweeps);

    /// <summary>Số lần quét có batch lỗi; phần việc lỗi được thử lại ở lần quét kế tiếp.</summary>
    public long FailedSweeps => Interlocked.Read(ref _failedSweeps);

    /// <summary>Chạy tới khi bị hủy (tắt dần): hoàn tất việc đang làm hoặc hoàn tác transaction, không để lại nửa chừng.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SweepOnceAsync(stoppingToken);
            try { await Task.Delay(Interval, clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Một lần quét gồm nhiều batch cho tới khi hết đơn quá hạn; trả số đơn đã hủy (kể cả khi lỗi giữa chừng).</summary>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        Guid sweepId = Guid.NewGuid();
        int total = 0;
        try
        {
            for (int batch = 0; batch < MaxBatchesPerSweep; batch++)
            {
                using IServiceScope scope = scopes.CreateScope();
                int expired = await scope.ServiceProvider.GetRequiredService<PaymentMaintenance>().ExpireAsync(cancellationToken);
                total += expired;
                if (expired < PaymentMaintenance.BatchSize) break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return total; }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _failedSweeps);
            OrderExpiryMetrics.RecordFailure();
            LogSweepFailed(logger, sweepId, exception);
        }
        await CleanupIfDueAsync(sweepId, cancellationToken);
        Interlocked.Increment(ref _completedSweeps);
        return total;
    }

    private async Task CleanupIfDueAsync(Guid sweepId, CancellationToken cancellationToken)
    {
        if (clock.GetUtcNow() < _nextCleanup) return;
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PaymentMaintenance>().CleanupAsync(cancellationToken);
            _nextCleanup = clock.GetUtcNow() + CleanupInterval;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _failedSweeps);
            OrderExpiryMetrics.RecordFailure();
            LogCleanupFailed(logger, sweepId, exception);
        }
    }

    [LoggerMessage(LogLevel.Error, "Order expiry sweep {SweepId} failed; unexpired work stays pending and the next sweep retries")]
    private static partial void LogSweepFailed(ILogger logger, Guid sweepId, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Expired access record cleanup {SweepId} failed; it will be retried on the next sweep")]
    private static partial void LogCleanupFailed(ILogger logger, Guid sweepId, Exception exception);
}
