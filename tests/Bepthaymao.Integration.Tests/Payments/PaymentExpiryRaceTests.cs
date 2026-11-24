using System.Net;
using System.Net.Http.Json;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Payments;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Payments;

/// <summary>
/// Hết hạn chạy đồng thời với webhook, xác nhận thủ công và khách hủy trên PostgreSQL thật (PAY-06/PAY-07, T-03-05).
/// Mỗi vòng cho cả hai bên xuất phát cùng lúc; kết quả hợp lệ có thể là một trong hai thứ tự, bất biến phải đúng ở cả hai.
/// </summary>
public sealed class PaymentExpiryRaceTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    private const int _rounds = 8;
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await PaymentWorld.CreateAsync(infrastructure);
    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private async Task<int> SweepAsync()
    {
        using IServiceScope scope = _world.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentMaintenance>().ExpireAsync(Token);
    }

    private static async Task<T> StartTogetherAsync<T>(TaskCompletionSource gate, Func<Task<T>> work)
    {
        await gate.Task;
        return await work();
    }

    private async Task<(int Expired, HttpResponseMessage Other)> RaceAsync(Func<Task<HttpResponseMessage>> other)
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> expiry = Task.Run(() => StartTogetherAsync(gate, SweepAsync), Token);
        Task<HttpResponseMessage> request = Task.Run(() => StartTogetherAsync(gate, other), Token);
        gate.SetResult();
        return (await expiry, await request);
    }

    private async Task<int> CountEventsAsync(string type, Guid orderId)
    {
        using IServiceScope scope = _world.OpenScope();
        List<string> payloads = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .Where(value => value.Type == type).Select(value => value.Payload).ToListAsync(Token);
        return payloads.Count(payload => payload.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task AssertPaidExactlyOnceAsync(Order order, int expectedPayments)
    {
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order final = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Paid, final.Status);
        Assert.Null(final.CancellationKind);
        Assert.Null(final.CancelReason);
        Assert.Equal(expectedPayments, await db.Payments.CountAsync(value => value.OrderId == order.Id, Token));
        Assert.Equal(1, await CountEventsAsync("order.paid", order.Id));
        // order.expired chỉ phát khi Worker thắng lượt; khi đó đơn phải được khôi phục, ngược lại không có sự kiện nào.
        Assert.Equal(final.RestoredFromExpiry ? 1 : 0, await CountEventsAsync("order.expired", order.Id));
        Assert.Equal(0, await CountEventsAsync("order.status_changed", order.Id));
    }

    [Fact]
    public async Task ExpiryRacingTheWebhookNeverLosesThePaymentOrCancelsAPaidOrder()
    {
        for (int round = 1; round <= _rounds; round++)
        {
            Order order = await _world.PlaceAsync(minutesAgo: 16);
            (int expired, HttpResponseMessage response) = await RaceAsync(() => _world.SendAsync(_world.Transfer(order, round)));
            using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.InRange(expired, 0, 1);
            await AssertPaidExactlyOnceAsync(order, 1);
        }
        using IServiceScope scope = _world.OpenScope();
        Assert.Equal(_rounds, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().DishStats.SingleAsync(Token)).OrderCount);
    }

    [Fact]
    public async Task ExpiryRacingOwnerManualConfirmationAlwaysEndsPaidOnce()
    {
        using HttpClient owner = await _world.LoginAsync("owner-a");
        for (int round = 0; round < _rounds; round++)
        {
            Order order = await _world.PlaceAsync(minutesAgo: 16);
            (int expired, HttpResponseMessage response) = await RaceAsync(() =>
                owner.PostAsJsonAsync($"/api/v1/owner/orders/{order.Id}/confirm-payment", new { }, Token));
            using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.InRange(expired, 0, 1);
            await AssertPaidExactlyOnceAsync(order, 1);
        }
    }

    [Fact]
    public async Task ExpiryRacingCustomerCancelLeavesExactlyOneCancellationKindAndMatchingEvents()
    {
        using HttpClient customer = _world.Factory.CreateClient();
        for (int round = 0; round < _rounds; round++)
        {
            Order order = await _world.PlaceAsync(minutesAgo: 16);
            (int expired, HttpResponseMessage response) = await RaceAsync(() =>
                customer.PostAsJsonAsync($"/api/v1/orders/{order.Code}/cancel", new { code = order.Code, phone = PaymentWorld.Phone }, Token));
            using (response)
            {
                using IServiceScope scope = _world.OpenScope();
                Order final = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
                Assert.Equal(OrderStatus.Cancelled, final.Status);
                if (final.CancellationKind == CancellationKind.ByCustomer)
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Equal(0, expired);
                    Assert.Equal(0, await CountEventsAsync("order.expired", order.Id));
                    Assert.Equal(1, await CountEventsAsync("order.status_changed", order.Id));
                }
                else
                {
                    Assert.Equal(CancellationKind.Expired, final.CancellationKind);
                    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                    Assert.Equal(1, expired);
                    Assert.Equal(1, await CountEventsAsync("order.expired", order.Id));
                    Assert.Equal(0, await CountEventsAsync("order.status_changed", order.Id));
                }
            }
        }
    }

    [Fact]
    public async Task LateMoneyRestoresAnExpiredOrderButAnActivelyCancelledOrderStaysCancelled()
    {
        Order expired = await _world.PlaceAsync(minutesAgo: 16);
        Order cancelled = await _world.PlaceAsync(minutesAgo: 16);
        using (IServiceScope scope = _world.OpenScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Orders.SingleAsync(value => value.Id == cancelled.Id, Token)).CancelByCustomer(_world.Clock.GetUtcNow());
            await db.SaveChangesAsync(Token);
        }
        Assert.Equal(1, await SweepAsync());
        using (HttpResponseMessage one = await _world.SendAsync(_world.Transfer(expired, 1)))
        using (HttpResponseMessage two = await _world.SendAsync(_world.Transfer(cancelled, 2)))
        {
            Assert.Equal(HttpStatusCode.OK, one.StatusCode);
            Assert.Equal(HttpStatusCode.OK, two.StatusCode);
        }
        using IServiceScope check = _world.OpenScope();
        AppDbContext verify = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Order restored = await verify.Orders.SingleAsync(value => value.Id == expired.Id, Token);
        Assert.Equal(OrderStatus.Paid, restored.Status);
        Assert.True(restored.RestoredFromExpiry);
        Assert.True(restored.LatePayment);
        Order kept = await verify.Orders.SingleAsync(value => value.Id == cancelled.Id, Token);
        Assert.Equal(OrderStatus.Cancelled, kept.Status);
        Assert.Equal(CancellationKind.ByCustomer, kept.CancellationKind);
        Assert.Equal(1, (await verify.DishStats.SingleAsync(Token)).OrderCount);
        Assert.Equal(2, await verify.Payments.CountAsync(Token));
    }

    [Fact]
    public async Task TwoWorkersAndManyWebhooksTogetherSettleEveryOrderExactlyOnce()
    {
        const int total = 20;
        List<Order> orders = [];
        for (int index = 0; index < total; index++) orders.Add(await _world.PlaceAsync(minutesAgo: 16));
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> DrainAsync()
        {
            await gate.Task;
            int sum = 0;
            for (int pass = 0; pass < 5; pass++) sum += await SweepAsync();
            return sum;
        }
        List<Task> work = [Task.Run(DrainAsync, Token), Task.Run(DrainAsync, Token)];
        List<Task<HttpResponseMessage>> webhooks = [];
        for (int index = 0; index < total; index++)
        {
            Order order = orders[index];
            long id = index + 1;
            webhooks.Add(Task.Run(() => StartTogetherAsync(gate, () => _world.SendAsync(_world.Transfer(order, id))), Token));
        }
        gate.SetResult();
        await Task.WhenAll(work);
        foreach (HttpResponseMessage response in await Task.WhenAll(webhooks))
            using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (Order order in orders) await AssertPaidExactlyOnceAsync(order, 1);
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(total, (await db.DishStats.SingleAsync(Token)).OrderCount);
        Assert.Equal(total, await db.Payments.CountAsync(Token));
    }
}
