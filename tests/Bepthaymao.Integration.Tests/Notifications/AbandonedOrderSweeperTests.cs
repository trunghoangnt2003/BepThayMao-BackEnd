using Bepthaymao.Api.Security;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Orders;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Bepthaymao.Integration.Tests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bepthaymao.Integration.Tests.Notifications;

[Collection(SharedInfrastructure.Name)]
public sealed class AbandonedOrderSweeperTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await PaymentWorld.CreateAsync(infrastructure);
    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    /// <summary>Worker chạy với WorkerCurrentUser; trong Api factory, quyền hệ thống phải được cấp tường minh cho từng scope.</summary>
    private sealed class SystemScopeFactory(IServiceProvider services) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => services.CreateSystemScope();
    }

    private AbandonedOrderSweeper NewSweeper() =>
        new(new SystemScopeFactory(_world.Factory.Services), _world.Clock, NullLogger<AbandonedOrderSweeper>.Instance);

    private async Task<int> AbandonedEventsAsync(Guid orderId)
    {
        using IServiceScope scope = _world.OpenScope();
        List<string> payloads = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .Where(m => m.Type == "order.abandoned").Select(m => m.Payload).ToListAsync(Token);
        return payloads.Count(payload => payload.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PaidOrderPastThresholdAlertsExactlyOnce()
    {
        Order order = await NotificationWorld.PlacePaidAsync(_world, paidMinutesAgo: 11);
        AbandonedOrderSweeper sweeper = NewSweeper();

        Assert.Equal(1, await sweeper.SweepOnceAsync(Token));
        Assert.Equal(0, await sweeper.SweepOnceAsync(Token));
        Assert.Equal(1, await AbandonedEventsAsync(order.Id));
    }

    [Fact]
    public async Task PaidOrderBeforeThresholdIsNotAlertedUntilThresholdPasses()
    {
        Order order = await NotificationWorld.PlacePaidAsync(_world, paidMinutesAgo: 5);
        AbandonedOrderSweeper sweeper = NewSweeper();

        Assert.Equal(0, await sweeper.SweepOnceAsync(Token));
        _world.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(1, await sweeper.SweepOnceAsync(Token));
        Assert.Equal(1, await AbandonedEventsAsync(order.Id));
    }

    [Fact]
    public async Task AcceptedOrderIsNeverAlerted()
    {
        Order order = await NotificationWorld.PlacePaidAsync(_world, paidMinutesAgo: 30);
        using (IServiceScope scope = _world.OpenScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Orders.SingleAsync(o => o.Id == order.Id, Token)).Accept(_world.Clock.GetUtcNow());
            await db.SaveChangesAsync(Token);
        }

        Assert.Equal(0, await NewSweeper().SweepOnceAsync(Token));
        Assert.Equal(0, await AbandonedEventsAsync(order.Id));
    }

    [Fact]
    public async Task UnpaidOrderIsNeverAlerted()
    {
        Order order = await _world.PlaceAsync(minutesAgo: 14);

        Assert.Equal(0, await NewSweeper().SweepOnceAsync(Token));
        Assert.Equal(0, await AbandonedEventsAsync(order.Id));
    }
}
