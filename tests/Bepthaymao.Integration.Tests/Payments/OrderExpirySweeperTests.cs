using System.Diagnostics.Metrics;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Identity;
using Bepthaymao.Infrastructure.Orders;
using Bepthaymao.Infrastructure.Payments;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Bepthaymao.Integration.Tests.Payments;

/// <summary>
/// Vòng quét của Worker (15 giây, tắt dần, thử lại sau lỗi) và dọn bản ghi hết hạn, điều khiển bằng thời gian giả:
/// code được kiểm không dùng Task.Delay thật; chỉ phần chờ điều kiện của test mới poll thời gian thật.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class OrderExpirySweeperTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await PaymentWorld.CreateAsync(infrastructure);
    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    /// <summary>Bọc thời gian giả và đếm số lần tạo timer, để test biết vòng quét đã chờ xong trước khi tua giờ.</summary>
    private sealed class ObservableTimeProvider(FakeTimeProvider inner) : TimeProvider
    {
        private int _timers;
        public int Timers => Volatile.Read(ref _timers);
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _ = Interlocked.Increment(ref _timers);
            return inner.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class CapturingLogger : ILogger<OrderExpirySweeper>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];
        public IReadOnlyList<(LogLevel Level, string Message)> Entries { get { lock (_entries) return [.. _entries]; } }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private (OrderExpirySweeper Sweeper, ObservableTimeProvider Time, CapturingLogger Logger) NewSweeper()
    {
        ObservableTimeProvider time = new(_world.Clock);
        CapturingLogger logger = new();
        return (new(_world.Factory.Services.GetRequiredService<IServiceScopeFactory>(), time, logger), time, logger);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 600; attempt++)
        {
            if (condition()) return;
            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }
        Assert.Fail("Điều kiện không đạt trong 30 giây.");
    }

    private async Task<OrderStatus> StatusAsync(Order order)
    {
        using IServiceScope scope = _world.OpenScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token)).Status;
    }

    private async Task<int> SweepAsync()
    {
        using IServiceScope scope = _world.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentMaintenance>().ExpireAsync(Token);
    }

    [Fact]
    public async Task LoopSweepsEveryFifteenSecondsCancelsAtExactlyFifteenMinutesAndStopsGracefully()
    {
        Order order = await _world.PlaceAsync();
        (OrderExpirySweeper sweeper, ObservableTimeProvider time, _) = NewSweeper();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task run = Task.Run(() => sweeper.RunAsync(stop.Token), Token);
        await WaitUntilAsync(() => time.Timers >= 1);
        Assert.Equal(1, sweeper.CompletedSweeps);

        _world.Clock.Advance(Order.PaymentWindow - OrderExpirySweeper.Interval);
        await WaitUntilAsync(() => time.Timers >= 2);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(order));

        _world.Clock.Advance(OrderExpirySweeper.Interval);
        await WaitUntilAsync(() => time.Timers >= 3);
        Assert.Equal(OrderStatus.Cancelled, await StatusAsync(order));
        Assert.True(sweeper.CompletedSweeps >= 3);

        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(0, sweeper.FailedSweeps);
    }

    [Fact]
    public async Task FailedSweepIsLoggedWithoutPersonalDataCountedAndRetriedWhileTheLoopStaysAlive()
    {
        Order order = await _world.PlaceAsync(minutesAgo: 20);
        using (IServiceScope scope = _world.OpenScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_expiry_commit() RETURNS trigger AS $$
                BEGIN RAISE EXCEPTION 'injected failure at commit'; END $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER fail_expiry_commit AFTER INSERT ON outbox_messages
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.type = 'order.expired') EXECUTE FUNCTION fail_expiry_commit();
                """, Token);
        }
        (OrderExpirySweeper sweeper, ObservableTimeProvider time, CapturingLogger logger) = NewSweeper();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task run = Task.Run(() => sweeper.RunAsync(stop.Token), Token);
        await WaitUntilAsync(() => time.Timers >= 1);

        Assert.Equal(1, sweeper.FailedSweeps);
        Assert.False(run.IsCompleted);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(order));
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.DoesNotContain(order.Code, message, StringComparison.Ordinal);
        Assert.DoesNotContain(PaymentWorld.Phone, message, StringComparison.Ordinal);

        using (IServiceScope scope = _world.OpenScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("DROP TRIGGER fail_expiry_commit ON outbox_messages", Token);
        _world.Clock.Advance(OrderExpirySweeper.Interval);
        await WaitUntilAsync(() => time.Timers >= 2);
        Assert.Equal(OrderStatus.Cancelled, await StatusAsync(order));
        Assert.Equal(1, sweeper.FailedSweeps);
        Assert.False(run.IsCompleted);

        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task OneSweepDrainsABacklogLargerThanOneBatchAndTheNextFindsNothing()
    {
        _ = await _world.PlaceManyAsync(PaymentMaintenance.BatchSize + 20, minutesAgo: 20);
        (OrderExpirySweeper sweeper, _, _) = NewSweeper();
        Assert.Equal(PaymentMaintenance.BatchSize + 20, await sweeper.SweepOnceAsync(Token));
        Assert.Equal(0, await sweeper.SweepOnceAsync(Token));
        Assert.Equal(2, sweeper.CompletedSweeps);
        Assert.Equal(0, sweeper.FailedSweeps);
    }

    [Fact]
    public async Task ExpiryLagAndCountAreExposedAsMeasurements()
    {
        // Độ trễ 37,37 giây là giá trị riêng của test này (Meter dùng chung toàn process).
        const double lagSeconds = 37.37;
        List<double> lags = [];
        List<long> counts = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == OrderExpiryMetrics.MeterName) meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => { lock (lags) lags.Add(value); });
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == OrderExpiryMetrics.ExpiredInstrument) lock (counts) counts.Add(value);
        });
        listener.Start();
        _ = await _world.PlaceAsync(minutesAgo: 15);
        _world.Clock.Advance(TimeSpan.FromSeconds(lagSeconds));
        Assert.Equal(1, await SweepAsync());
        lock (lags) Assert.Contains(lags, value => Math.Abs(value - lagSeconds) < 0.001);
        lock (counts) Assert.NotEmpty(counts);
    }

    [Fact]
    public async Task CleanupRemovesOnlyExpiredGrantsAndRefreshTokensInBoundedBatches()
    {
        Order order = await _world.PlaceAsync();
        DateTimeOffset now = _world.Clock.GetUtcNow();
        Guid user;
        using (IServiceScope scope = _world.OpenScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            user = await db.Users.Select(value => value.Id).FirstAsync(Token);
            for (int index = 0; index < 150; index++)
                db.OrderAccessGrants.Add(new() { Id = Guid.CreateVersion7(), OrderId = order.Id, TokenHash = "e" + index.ToString("D63", System.Globalization.CultureInfo.InvariantCulture), CreatedAt = now.AddDays(-2), ExpiresAt = now.AddMinutes(-1 - index) });
            db.OrderAccessGrants.Add(new() { Id = Guid.CreateVersion7(), OrderId = order.Id, TokenHash = "valid-grant", CreatedAt = now, ExpiresAt = now.AddHours(1) });
            db.RefreshTokens.AddRange(
                new RefreshToken { Id = Guid.CreateVersion7(), UserId = user, FamilyId = Guid.NewGuid(), TokenHash = "old-token", CreatedAt = now.AddDays(-9), ExpiresAt = now.AddDays(-2) },
                new RefreshToken { Id = Guid.CreateVersion7(), UserId = user, FamilyId = Guid.NewGuid(), TokenHash = "just-expired-token", CreatedAt = now.AddDays(-8), ExpiresAt = now.AddHours(-1) },
                new RefreshToken { Id = Guid.CreateVersion7(), UserId = user, FamilyId = Guid.NewGuid(), TokenHash = "valid-token", CreatedAt = now, ExpiresAt = now.AddDays(7) });
            await db.SaveChangesAsync(Token);
        }
        async Task<int> CleanupAsync()
        {
            using IServiceScope scope = _world.OpenScope();
            return await scope.ServiceProvider.GetRequiredService<PaymentMaintenance>().CleanupAsync(Token);
        }
        // Lần 1: 100 grant (trần batch) + 1 token quá hạn ngày; lần 2: 50 grant còn lại.
        Assert.Equal(PaymentMaintenance.BatchSize + 1, await CleanupAsync());
        Assert.Equal(50, await CleanupAsync());
        Assert.Equal(0, await CleanupAsync());
        using IServiceScope check = _world.OpenScope();
        AppDbContext verify = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(["valid-grant"], await verify.OrderAccessGrants.Select(value => value.TokenHash).ToListAsync(Token));
        Assert.Equal(["just-expired-token", "valid-token"], await verify.RefreshTokens.Select(value => value.TokenHash).Order().ToListAsync(Token));
    }
}
