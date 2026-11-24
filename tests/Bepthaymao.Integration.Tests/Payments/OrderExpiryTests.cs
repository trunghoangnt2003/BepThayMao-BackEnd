using System.Text.Json;
using Bepthaymao.Application.Orders;
using Bepthaymao.Domain.Events;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Payments;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Payments;

/// <summary>
/// Quét hết hạn đơn chưa thanh toán (PAY-06) trên PostgreSQL thật với FakeTimeProvider: không dùng Task.Delay thật,
/// không dùng SQLite/in-memory để chứng minh khóa dòng.
/// </summary>
public sealed class OrderExpiryTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    // Kích thước batch của PaymentMaintenance: giữ transaction ngắn và công việc mỗi lần quét có giới hạn.
    private const int _batch = 100;
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await PaymentWorld.CreateAsync(infrastructure);
    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    // PostgreSQL timestamptz lưu tới micro giây; FakeTimeProvider có độ chính xác 100 ns.
    private static DateTimeOffset Micro(DateTimeOffset value) => new(value.Ticks - (value.Ticks % 10), value.Offset);

    private async Task<int> SweepAsync()
    {
        using IServiceScope scope = _world.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentMaintenance>().ExpireAsync(Token);
    }

    private async Task<List<Order>> PlaceManyAsync(int count, int minutesAgo)
    {
        List<Order> orders = [];
        for (int index = 0; index < count; index++)
        {
            Order order = Order.Place(Guid.NewGuid(), OrderCodes.Next(), _world.BranchA, new("Test", PaymentWorld.Phone, "Test", null, null, null, null),
                [OrderItem.Create(Guid.NewGuid(), _world.DishId, "Xôi", 40000, 1)], false, new(2026, 11, 23), _world.Clock.GetUtcNow().AddMinutes(-minutesAgo));
            order.CapturePaymentDestination(PaymentWorld.BankCode, PaymentWorld.SharedAccount);
            orders.Add(order);
        }
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync(Token);
        return orders;
    }

    private async Task<Order> ReloadAsync(Order order)
    {
        using IServiceScope scope = _world.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
    }

    private async Task<List<Guid>> ExpiredEventOrderIdsAsync()
    {
        using IServiceScope scope = _world.OpenScope();
        List<string> payloads = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .Where(value => value.Type == "order.expired").Select(value => value.Payload).ToListAsync(Token);
        return [.. payloads.Select(payload => JsonDocument.Parse(payload).RootElement.GetProperty("orderId").GetGuid())];
    }

    [Fact]
    public async Task OrderStaysPendingAt1459AndExpiresExactlyAt1500WithKindExpired()
    {
        Order order = await _world.PlaceAsync();
        _world.Clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Assert.Equal(0, await SweepAsync());
        Assert.Equal(OrderStatus.PendingPayment, (await ReloadAsync(order)).Status);
        Assert.Empty(await ExpiredEventOrderIdsAsync());

        _world.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await SweepAsync());
        Order expired = await ReloadAsync(order);
        Assert.Equal(OrderStatus.Cancelled, expired.Status);
        Assert.Equal(CancellationKind.Expired, expired.CancellationKind);
        Assert.Equal(Micro(_world.Clock.GetUtcNow()), expired.CancelledAt);
        Assert.Equal(Micro(order.CreatedAt.AddMinutes(15)), expired.ExpiresAt);
        Assert.Equal([order.Id], await ExpiredEventOrderIdsAsync());
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.OutboxMessages.CountAsync(value => value.Type == "order.status_changed", Token));
        Assert.Equal(0, await SweepAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync(value => value.Type == "order.expired", Token));
    }

    [Fact]
    public async Task NextDayOrderPlacedOutOfHoursStillExpiresAfterFifteenMinutes()
    {
        // Đơn đặt ngoài giờ cho ngày mai vẫn có cửa sổ thanh toán 15 phút, không đợi tới giờ mở cửa.
        // FakeTimeProvider không lùi được: nhảy tới 20:00 UTC kế tiếp (03:00 giờ Việt Nam, ngoài giờ bán).
        DateTimeOffset now = _world.Clock.GetUtcNow();
        DateTimeOffset target = new(now.Year, now.Month, now.Day, 20, 0, 0, TimeSpan.Zero);
        _world.Clock.SetUtcNow(target > now ? target : target.AddDays(1));
        Order order = await _world.PlaceAsync(nextDay: true);
        _world.Clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Assert.Equal(0, await SweepAsync());
        _world.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await SweepAsync());
        Order expired = await ReloadAsync(order);
        Assert.True(expired.IsNextDay);
        Assert.Equal(CancellationKind.Expired, expired.CancellationKind);
    }

    [Fact]
    public async Task PaidOrdersAndOrdersAlreadyCancelledAreNeverExpired()
    {
        Order paid = await _world.PlaceAsync();
        Order cancelled = await _world.PlaceAsync();
        using (HttpResponseMessage response = await _world.SendAsync(_world.Transfer(paid)))
            Assert.True(response.IsSuccessStatusCode);
        using (IServiceScope scope = _world.OpenScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Orders.SingleAsync(value => value.Id == cancelled.Id, Token)).CancelByCustomer(_world.Clock.GetUtcNow());
            await db.SaveChangesAsync(Token);
        }
        _world.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0, await SweepAsync());
        Assert.Equal(OrderStatus.Paid, (await ReloadAsync(paid)).Status);
        Assert.Equal(CancellationKind.ByCustomer, (await ReloadAsync(cancelled)).CancellationKind);
        Assert.Empty(await ExpiredEventOrderIdsAsync());
    }

    [Fact]
    public async Task ExpiryWritesAnAuditRowWithoutAnActor()
    {
        Order order = await _world.PlaceAsync(minutesAgo: 16);
        Assert.Equal(1, await SweepAsync());
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Domain.Payments.PaymentAudit? audit = await db.PaymentAudits.SingleOrDefaultAsync(value => value.OrderId == order.Id && value.Action == "order_expired", Token);
        Assert.NotNull(audit);
        Assert.Null(audit.ActorId);
        Assert.Equal(Micro(_world.Clock.GetUtcNow()), audit.OccurredAt);
    }

    [Fact]
    public async Task OneSweepIsBoundedAndTheNextSweepFinishesTheBacklog()
    {
        await PlaceManyAsync(_batch + 50, minutesAgo: 20);
        Assert.Equal(_batch, await SweepAsync());
        Assert.Equal(50, await SweepAsync());
        Assert.Equal(0, await SweepAsync());
        Assert.Equal(_batch + 50, (await ExpiredEventOrderIdsAsync()).Distinct().Count());
    }

    [Fact]
    public async Task TwoConcurrentWorkersExpireEachOrderExactlyOnceWithOneEventEach()
    {
        const int total = 60;
        List<Order> orders = await PlaceManyAsync(total, minutesAgo: 20);
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> DrainAsync()
        {
            await gate.Task;
            int sum = 0;
            for (int pass = 0; pass < 20; pass++)
            {
                int expired = await SweepAsync();
                sum += expired;
                if (expired == 0 && pass > 0) break;
            }
            return sum;
        }
        Task<int> first = Task.Run(DrainAsync, Token);
        Task<int> second = Task.Run(DrainAsync, Token);
        gate.SetResult();
        int[] counts = await Task.WhenAll(first, second);
        // Mỗi đơn chỉ được một worker xử lý: tổng bằng đúng số đơn, không đơn nào đếm hai lần.
        Assert.Equal(total, counts.Sum());
        List<Guid> ids = await ExpiredEventOrderIdsAsync();
        Assert.Equal(total, ids.Count);
        Assert.Equal(orders.Select(value => value.Id).Order(), ids.Order());
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(total, await db.PaymentAudits.CountAsync(value => value.Action == "order_expired", Token));
        Assert.Equal(total, await db.Orders.CountAsync(value => value.Status == OrderStatus.Cancelled && value.CancellationKind == CancellationKind.Expired, Token));
    }

    [Fact]
    public async Task SweepSkipsRowsLockedByAnotherTransactionInsteadOfBlockingOrOverwriting()
    {
        Order order = await _world.PlaceAsync(minutesAgo: 20);
        using (IServiceScope holder = _world.OpenScope())
        {
            AppDbContext lockDb = holder.ServiceProvider.GetRequiredService<AppDbContext>();
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await lockDb.Database.BeginTransactionAsync(Token);
            await lockDb.Database.ExecuteSqlAsync($"SELECT id FROM orders WHERE id = {order.Id} FOR UPDATE", Token);
            Task<int> sweep = SweepAsync();
            // Không chờ khóa: lần quét xong ngay khi dòng đang bị giữ (SKIP LOCKED), nếu chờ thì quá 10 giây.
            Assert.Same(sweep, await Task.WhenAny(sweep, Task.Delay(TimeSpan.FromSeconds(10), Token)));
            Assert.Equal(0, await sweep);
            Assert.Equal(OrderStatus.PendingPayment, (await ReloadAsync(order)).Status);
            await tx.RollbackAsync(Token);
        }
        Assert.Equal(1, await SweepAsync());
    }

    [Fact]
    public async Task FailureAfterTheUpdateButBeforeCommitRollsEverythingBackAndTheNextSweepRetries()
    {
        List<Order> orders = await PlaceManyAsync(3, minutesAgo: 20);
        using (IServiceScope scope = _world.OpenScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Constraint trigger hoãn tới COMMIT: mọi UPDATE/INSERT của lần quét đã chạy xong rồi mới lỗi.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_expiry_commit() RETURNS trigger AS $$
                BEGIN RAISE EXCEPTION 'injected failure at commit'; END $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER fail_expiry_commit AFTER INSERT ON outbox_messages
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.type = 'order.expired') EXECUTE FUNCTION fail_expiry_commit();
                """, Token);
        }
        _ = await Assert.ThrowsAnyAsync<Exception>(SweepAsync);
        using (IServiceScope check = _world.OpenScope())
        {
            AppDbContext db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(3, await db.Orders.CountAsync(value => value.Status == OrderStatus.PendingPayment, Token));
            Assert.Equal(0, await db.OutboxMessages.CountAsync(value => value.Type == "order.expired", Token));
            Assert.Equal(0, await db.PaymentAudits.CountAsync(value => value.Action == "order_expired", Token));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_expiry_commit ON outbox_messages", Token);
        }
        Assert.Equal(3, await SweepAsync());
        Assert.Equal(orders.Select(value => value.Id).Order(), (await ExpiredEventOrderIdsAsync()).Order());
    }

    [Fact]
    public async Task ExpiredEventStaysUnpublishedInTheOutboxWithTheContractPayload()
    {
        Order order = await _world.PlaceAsync(minutesAgo: 16);
        Assert.Equal(1, await SweepAsync());
        using IServiceScope scope = _world.OpenScope();
        OutboxMessage message = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .SingleAsync(value => value.Type == "order.expired", Token);
        Assert.Null(message.PublishedAt);
        using JsonDocument payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(order.Id, payload.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal(order.Code, payload.RootElement.GetProperty("code").GetString());
        Assert.Equal(_world.BranchA, payload.RootElement.GetProperty("branchId").GetGuid());
    }
}
