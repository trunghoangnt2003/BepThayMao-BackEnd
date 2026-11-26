using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Menu;
using Bepthaymao.Application.Orders;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Events;
using Bepthaymao.Domain.Menu;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Domain.Owners;
using Bepthaymao.Domain.Payments;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Orders;

/// <summary>Lifecycle thanh toán lưu bền (03-01): snapshot ngân hàng, kind hủy, DishStat một lần, outbox một chủ sở hữu.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class PaymentPersistenceTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string _bank = "970436";
    private const string _account = "0000000001";
    private static readonly DateTimeOffset _now = new(2026, 11, 23, 3, 0, 0, TimeSpan.Zero);
    private readonly Guid _owner = Guid.CreateVersion7();
    private readonly Guid _branch = Guid.CreateVersion7();
    private readonly Guid _dish = Guid.CreateVersion7();
    private ApiFactory _factory = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
            ["RateLimiting:Public:PermitLimit"] = "1000",
        });
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(Token);
        db.Owners.Add(Owner.Create(_owner, "Owner persistence test", _now));
        db.Branches.Add(Branch.Create(_branch, _owner,
            new("Cơ sở thử", "1 Đường Thử", "0901234567", 10.77, 106.70, new(0, 0), new(23, 59), 10, null, _account, _bank, null, true), _now));
        db.Dishes.Add(Dish.Create(_dish, _owner, new("Xôi xéo", "Đậu xanh", 25000, null, DishCategory.XoiMan, true, 0), _now));
        await db.SaveChangesAsync(Token);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private Order NewOrder(int quantity = 2)
    {
        Order order = Order.Place(Guid.CreateVersion7(), OrderCodes.Next(), _branch,
            new("Khách thử", "0901234567", "3 Đường Thử", null, null, null, null),
            [OrderItem.Create(Guid.CreateVersion7(), _dish, "Xôi xéo", 25000, quantity)], false, new(2026, 11, 23), _now);
        order.CapturePaymentDestination(_bank, _account);
        return order;
    }

    private async Task<Order> SaveNewOrderAsync(int quantity = 2)
    {
        Order order = NewOrder(quantity);
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(order);
        await db.SaveChangesAsync(Token);
        return order;
    }

    private async Task MutateAsync(Guid orderId, Action<Order> change)
    {
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order order = await db.Orders.Include(value => value.Items).SingleAsync(value => value.Id == orderId, Token);
        change(order);
        await db.SaveChangesAsync(Token);
    }

    private async Task<Order> ReloadAsync(Guid orderId)
    {
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Orders.AsNoTracking().SingleAsync(value => value.Id == orderId, Token);
    }

    private async Task<List<OutboxMessage>> OutboxAsync(string? type = null)
    {
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OutboxMessages.AsNoTracking().Where(value => type == null || value.Type == type).ToListAsync(Token);
    }

    private async Task SetStatAsync(long count)
    {
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DishStats.Add(new() { DishId = _dish, BranchId = _branch, OrderCount = count });
        await db.SaveChangesAsync(Token);
    }

    private async Task<long> StatAsync()
    {
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DishStats.AsNoTracking().Where(value => value.DishId == _dish).Select(value => value.OrderCount).SingleAsync(Token);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }

    private async Task<(HttpClient Client, string Code, string QrUrl)> PlaceViaApiAsync()
    {
        HttpClient client = _factory.CreateClient();
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerName = "Khách thử",
            customerPhone = "0901 234 567",
            deliveryAddress = "3 Đường Thử",
            branchId = _branch,
            nextDayAcknowledged = false,
            items = new[] { new { dishId = _dish, quantity = 1 } },
        }, Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        JsonElement body = await ReadJsonAsync(created);
        return (client, body.GetProperty("order").GetProperty("code").GetString()!, body.GetProperty("payment").GetProperty("qrUrl").GetString()!);
    }

    [Fact]
    public async Task PlacingOrderSnapshotsBankAndWritesOneCreatedEventWithoutPersonalData()
    {
        (HttpClient client, string code, string qrAtCreate) = await PlaceViaApiAsync();
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order order = await db.Orders.AsNoTracking().SingleAsync(value => value.Code == code, Token);
        Assert.Equal(_bank, order.PaymentBankCode);
        Assert.Equal(_account, order.PaymentAccountNo);

        await db.Database.ExecuteSqlAsync($"UPDATE branches SET bank_account_no = '9999999999', bank_code = '970415' WHERE id = {_branch}", Token);
        using HttpResponseMessage tracking = await client.GetAsync($"/api/v1/orders/{code}?phone=0901234567", Token);
        Assert.Equal(qrAtCreate, (await ReadJsonAsync(tracking)).GetProperty("payment").GetProperty("qrUrl").GetString());

        OutboxMessage created = Assert.Single(await OutboxAsync("order.created"));
        Assert.Equal(order.CreatedAt, created.OccurredAt);
        Assert.DoesNotContain("Khách thử", created.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("0901", created.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Đường Thử", created.Payload, StringComparison.Ordinal);
        using JsonDocument payload = JsonDocument.Parse(created.Payload);
        Assert.Equal(code, payload.RootElement.GetProperty("code").GetString());
        Assert.Equal(25000, payload.RootElement.GetProperty("itemsTotal").GetInt64());
    }

    [Fact]
    public async Task CustomerCancelExposesKindAndEmitsExactlyOneStatusChange()
    {
        (HttpClient client, string code, _) = await PlaceViaApiAsync();
        using HttpResponseMessage cancelled = await client.PostAsJsonAsync($"/api/v1/orders/{code}/cancel", new { code, phone = "0901234567" }, Token);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        JsonElement order = (await ReadJsonAsync(cancelled)).Clone();
        Assert.True(order.TryGetProperty("cancellationKind", out JsonElement kind), "OrderDto phải có cancellationKind theo contract 1.7.0.");
        Assert.Equal("ByCustomer", kind.GetString());
        Assert.True(order.TryGetProperty("paidAt", out JsonElement paidAt), "OrderDto phải có paidAt theo contract 1.7.0.");
        Assert.Equal(JsonValueKind.Null, paidAt.ValueKind);

        OutboxMessage changed = Assert.Single(await OutboxAsync("order.status_changed"));
        using JsonDocument payload = JsonDocument.Parse(changed.Payload);
        Assert.Equal("PendingPayment", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal("Cancelled", payload.RootElement.GetProperty("to").GetString());
        Assert.DoesNotContain("Khách thử", changed.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PaidAndExpiredTransitionsAreNotAlsoEmittedAsGenericStatusChange()
    {
        Order paying = await SaveNewOrderAsync();
        Order expiring = await SaveNewOrderAsync();
        await MutateAsync(paying.Id, order => Assert.True(order.ReceivePayment(50000, _now.AddMinutes(1), _now.AddMinutes(1))));
        await MutateAsync(expiring.Id, order => Assert.True(order.Expire(_now.AddMinutes(15))));
        Assert.Empty(await OutboxAsync("order.status_changed"));
        Assert.Equal(2, (await OutboxAsync("order.created")).Count);
    }

    [Fact]
    public async Task CancellationKindPaidAtAndReasonSurviveReload()
    {
        Order expired = await SaveNewOrderAsync();
        Order byCustomer = await SaveNewOrderAsync();
        Order byStaff = await SaveNewOrderAsync();
        Order restored = await SaveNewOrderAsync();
        await MutateAsync(expired.Id, order => order.Expire(_now.AddMinutes(15)));
        await MutateAsync(byCustomer.Id, order => order.CancelByCustomer(_now.AddMinutes(1)));
        await MutateAsync(byStaff.Id, order =>
        {
            order.ReceivePayment(50000, _now.AddMinutes(1), _now.AddMinutes(1));
            order.CancelByStaff(CancelReasonCode.Other, "Hết nguyên liệu", _now.AddMinutes(3));
        });
        await MutateAsync(restored.Id, order =>
        {
            order.Expire(_now.AddMinutes(15));
            order.ReceivePayment(50000, _now.AddMinutes(16), _now.AddMinutes(17));
        });

        Assert.Equal(CancellationKind.Expired, (await ReloadAsync(expired.Id)).CancellationKind);
        Assert.Null((await ReloadAsync(expired.Id)).CancelReason);
        Assert.Equal(CancellationKind.ByCustomer, (await ReloadAsync(byCustomer.Id)).CancellationKind);
        Order staff = await ReloadAsync(byStaff.Id);
        Assert.Equal(CancellationKind.ByStaff, staff.CancellationKind);
        Assert.Equal("Hết nguyên liệu", staff.CancelReason);
        Assert.Equal(_now.AddMinutes(1), staff.PaidAt);
        Order back = await ReloadAsync(restored.Id);
        Assert.Equal(OrderStatus.Paid, back.Status);
        Assert.Equal(PaymentStatus.LatePaid, back.PaymentStatus);
        Assert.Null(back.CancellationKind);
        Assert.Equal(_now.AddMinutes(16), back.PaidAt);
    }

    [Fact]
    public async Task CancellingPaidOrderDecrementsDishStatOnceAndUnpaidCancelNeverDoes()
    {
        await SetStatAsync(2);
        Order paid = await SaveNewOrderAsync(quantity: 2);
        Order unpaid = await SaveNewOrderAsync(quantity: 3);
        await MutateAsync(unpaid.Id, order => order.CancelByCustomer(_now.AddMinutes(1)));
        Assert.Equal(2, await StatAsync());

        await MutateAsync(paid.Id, order => order.ReceivePayment(50000, _now.AddMinutes(1), _now.AddMinutes(1)));
        await MutateAsync(paid.Id, order => order.CancelByStaff(CancelReasonCode.Other, "Quán đóng cửa", _now.AddMinutes(2)));
        Assert.Equal(0, await StatAsync());
        await Assert.ThrowsAsync<Bepthaymao.Domain.Common.DomainRuleException>(() =>
            MutateAsync(paid.Id, order => order.CancelByStaff(CancelReasonCode.Other, "Lần hai", _now.AddMinutes(3))));
        Assert.Equal(0, await StatAsync());
    }

    [Fact]
    public async Task MenuAvailabilityReadsDishStatTotalsAcrossBranches()
    {
        await SetStatAsync(7);
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        IMenuStore store = scope.ServiceProvider.GetRequiredService<IMenuStore>();
        IReadOnlyDictionary<Guid, long> paid = await store.PaidQuantitiesAsync(Token);
        Assert.Equal(7, paid[_dish]);
    }

    [Fact]
    public async Task PaymentLedgerAllowsUnmatchedRowsAndRejectsDuplicatesAndNonPositiveAmounts()
    {
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Payments.AddRange(
            new Payment { Id = Guid.CreateVersion7(), Amount = 5_000_000_000, SePayTransactionId = 10, OccurredAt = _now, ReceivedAt = _now },
            new Payment { Id = Guid.CreateVersion7(), Amount = 1, OccurredAt = _now, ReceivedAt = _now, Method = "MANUAL" },
            new Payment { Id = Guid.CreateVersion7(), Amount = 2, OccurredAt = _now, ReceivedAt = _now, Method = "MANUAL" });
        await db.SaveChangesAsync(Token);
        Assert.Equal(5_000_000_000, await db.Payments.Where(value => value.SePayTransactionId == 10).Select(value => value.Amount).SingleAsync(Token));
        Assert.Null((await db.Payments.AsNoTracking().FirstAsync(value => value.SePayTransactionId == 10, Token)).OrderId);

        using IServiceScope other = _factory.Services.CreateSystemScope();
        AppDbContext duplicate = other.ServiceProvider.GetRequiredService<AppDbContext>();
        duplicate.Payments.Add(new Payment { Id = Guid.CreateVersion7(), Amount = 9, SePayTransactionId = 10, OccurredAt = _now, ReceivedAt = _now });
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(Token));

        using IServiceScope zero = _factory.Services.CreateSystemScope();
        AppDbContext zeroDb = zero.ServiceProvider.GetRequiredService<AppDbContext>();
        zeroDb.Payments.Add(new Payment { Id = Guid.CreateVersion7(), Amount = 0, SePayTransactionId = 11, OccurredAt = _now, ReceivedAt = _now });
        await Assert.ThrowsAsync<DbUpdateException>(() => zeroDb.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task InboxIsKeyedByConsumerAndEventId()
    {
        Guid eventId = Guid.CreateVersion7();
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.InboxMessages.AddRange(new InboxMessage { Consumer = "inapp.q", EventId = eventId, ProcessedAt = _now },
            new InboxMessage { Consumer = "notifier.q", EventId = eventId, ProcessedAt = _now });
        await db.SaveChangesAsync(Token);
        using IServiceScope other = _factory.Services.CreateSystemScope();
        AppDbContext duplicate = other.ServiceProvider.GetRequiredService<AppDbContext>();
        duplicate.InboxMessages.Add(new InboxMessage { Consumer = "inapp.q", EventId = eventId, ProcessedAt = _now });
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task OrderAlreadyAcceptedIsNotMovedBackToPaidAndEmitsNothing()
    {
        Order order = await SaveNewOrderAsync();
        using (IServiceScope scope = _factory.Services.CreateSystemScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlAsync($"UPDATE orders SET status = 'Accepted', payment_status = 'Paid' WHERE id = {order.Id}", Token);
        }
        await MutateAsync(order.Id, loaded => Assert.False(loaded.ReceivePayment(50000, _now.AddMinutes(30), _now.AddMinutes(30))));
        Assert.Equal(OrderStatus.Accepted, (await ReloadAsync(order.Id)).Status);
        Assert.Empty(await OutboxAsync("order.status_changed"));
    }

    private async Task InsertLegacyOrderAsync(AppDbContext db, string code, string status, string paymentStatus, string? cancelReason, int quantity)
    {
        Guid id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO orders (id, code, branch_id, customer_name, customer_phone, delivery_address, items_total, status, payment_status,
                is_next_day, delivery_date, expires_at, cancel_reason, created_at, dedupe_hash)
            VALUES ({id}, {code}, {_branch}, 'Khách cũ', '0901234567', 'Địa chỉ cũ', {25000L * quantity}, {status}, {paymentStatus},
                false, {new DateOnly(2026, 11, 20)}, {_now.AddMinutes(15)}, {cancelReason}, {_now}, '')
            """, Token);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO order_items (id, order_id, dish_id, dish_name, unit_price, quantity, line_total)
            VALUES ({Guid.CreateVersion7()}, {id}, {_dish}, 'Xôi xéo', 25000, {quantity}, {25000L * quantity})
            """, Token);
    }

    [Fact]
    public async Task MigrationsBackfillLegacyOrdersAndRollBackWithoutLosingCancelReason()
    {
        const string before = "20261122151947_AddBranchQrProvider";
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IMigrator migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(before, Token);
        await InsertLegacyOrderAsync(db, "BTMPAID22", "Paid", "Paid", null, 2);
        await InsertLegacyOrderAsync(db, "BTMPAID33", "Accepted", "Paid", null, 1);
        await InsertLegacyOrderAsync(db, "BTMEXP222", "Cancelled", "Unpaid", "expired", 1);
        await InsertLegacyOrderAsync(db, "BTMCUS222", "Cancelled", "Unpaid", "customer", 1);
        await InsertLegacyOrderAsync(db, "BTMPEND22", "PendingPayment", "Unpaid", null, 4);

        await migrator.MigrateAsync(null, Token);
        Assert.Equal(3, await db.Database.SqlQuery<long>($"SELECT order_count AS \"Value\" FROM dish_stats WHERE dish_id = {_dish} AND branch_id = {_branch}").SingleAsync(Token));
        Assert.Equal(_account, await db.Database.SqlQuery<string>($"SELECT payment_account_no AS \"Value\" FROM orders WHERE code = 'BTMPEND22'").SingleAsync(Token));
        Assert.Equal(_bank, await db.Database.SqlQuery<string>($"SELECT payment_bank_code AS \"Value\" FROM orders WHERE code = 'BTMPAID22'").SingleAsync(Token));
        Assert.Equal("Expired", await db.Database.SqlQuery<string>($"SELECT cancellation_kind AS \"Value\" FROM orders WHERE code = 'BTMEXP222'").SingleAsync(Token));
        Assert.Equal("ByCustomer", await db.Database.SqlQuery<string>($"SELECT cancellation_kind AS \"Value\" FROM orders WHERE code = 'BTMCUS222'").SingleAsync(Token));
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM orders WHERE cancel_reason IS NOT NULL").SingleAsync(Token));
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM orders WHERE cancellation_kind IS NOT NULL AND status <> 'Cancelled'").SingleAsync(Token));

        await migrator.MigrateAsync(before, Token);
        Assert.Equal("expired", await db.Database.SqlQuery<string>($"SELECT cancel_reason AS \"Value\" FROM orders WHERE code = 'BTMEXP222'").SingleAsync(Token));
        Assert.Equal("customer", await db.Database.SqlQuery<string>($"SELECT cancel_reason AS \"Value\" FROM orders WHERE code = 'BTMCUS222'").SingleAsync(Token));
        await migrator.MigrateAsync(null, Token);
        Assert.Equal(3, await db.Database.SqlQuery<long>($"SELECT order_count AS \"Value\" FROM dish_stats WHERE dish_id = {_dish}").SingleAsync(Token));
    }
}
