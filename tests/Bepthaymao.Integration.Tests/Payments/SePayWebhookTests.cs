using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Application.Orders;
using Bepthaymao.Application.Payments;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Menu;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Bepthaymao.Integration.Tests.Payments;

public sealed partial class SePayWebhookTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    private const string _key = "test-only-sepay-webhook-secret-1234";
    private const string _password = "test-payment-password-123";
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _branch = Guid.NewGuid();
    private readonly Guid _dish = Guid.NewGuid();
    private ApiFactory _factory = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
            ["ConnectionStrings:RabbitMq"] = infrastructure.RabbitConnectionString,
            ["SePay:WebhookKey"] = _key
        }, _clock);
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(Token);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "payment-owner", _password, UserRole.Owner, _owner);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "other-payment-owner", _password, UserRole.Owner, Guid.NewGuid());
        db.Branches.Add(Branch.Create(_branch, _owner, new("Test bank", "Test street", "0900000000", 10.7, 106.7,
            new(0, 0), new(23, 59), 10, [], "123456", "970415", null, true), _clock.GetUtcNow()));
        db.Dishes.Add(Dish.Create(_dish, _owner, new("Xôi", null, 40000, null, DishCategory.XoiMan, false, 0), _clock.GetUtcNow()));
        await db.SaveChangesAsync(Token);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "payment-manager", _password, UserRole.BranchManager, branchId: _branch);
    }
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Order> PlaceAsync(int minutesAgo = 0)
    {
        Order order = Order.Place(Guid.NewGuid(), OrderCodes.Next(), _branch, new("Test", "0900000000", "Test", null, null, null, null),
            [OrderItem.Create(Guid.NewGuid(), _dish, "Xôi", 40000, 1)], false, new(2026, 11, 23), _clock.GetUtcNow().AddMinutes(-minutesAgo));
        order.CapturePaymentDestination("970415", "123456");
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(order); await db.SaveChangesAsync(Token);
        return order;
    }
    private SePayTransfer Transfer(Order order, long id = 1, long amount = 40000) => new(id, "VietinBank",
        _clock.GetUtcNow().AddHours(7).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), "123456", order.Code, "in", amount);
    private async Task<HttpResponseMessage> SendAsync(SePayTransfer transfer, string key = _key)
    {
        using HttpClient client = _factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/webhooks/sepay") { Content = JsonContent.Create(transfer) };
        request.Headers.Authorization = new("Apikey", key);
        return await client.SendAsync(request, Token);
    }
    private async Task<HttpClient> LoginAsync(string identifier)
    {
        HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier, password = _password }, Token);
        response.EnsureSuccessStatusCode();
        JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        client.DefaultRequestHeaders.Authorization = new("Bearer", result.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task AuthValidationMatchingReplayAndLedgerAreAtomic()
    {
        Order order = await PlaceAsync();
        using HttpResponseMessage unauthorized = await SendAsync(Transfer(order), "wrong-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using HttpResponseMessage invalid = await SendAsync(Transfer(order) with { TransferAmount = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using HttpResponseMessage first = await SendAsync(Transfer(order, amount: 45000));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using HttpResponseMessage replay = await SendAsync(Transfer(order, amount: 45000));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order paid = await db.Orders.SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Paid, paid.Status); Assert.True(paid.Overpaid); Assert.Equal(5000, paid.OverpaidAmount);
        Assert.Equal(1, await db.Payments.CountAsync(Token));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(value => value.Type == "order.paid", Token));
        Assert.Equal(1, (await db.DishStats.SingleAsync(Token)).OrderCount);
        // Database invariant also protects the future staff-cancellation path, inside its own transaction.
        await db.Database.ExecuteSqlAsync($"UPDATE orders SET status = 'Cancelled' WHERE id = {order.Id}", Token);
        await db.Database.ExecuteSqlAsync($"UPDATE orders SET status = 'Cancelled' WHERE id = {order.Id}", Token);
        Assert.Equal(0, await db.DishStats.Select(value => value.OrderCount).SingleAsync(Token));

    }

    [Fact]
    public async Task InsufficientWrongAccountAndMultipleCodesNeverSettle()
    {
        Order order = await PlaceAsync();
        foreach (SePayTransfer transfer in new[] { Transfer(order, 1, 39999), Transfer(order, 2) with { AccountNumber = "wrong" },
            Transfer(order, 3) with { Content = order.Code + " BTMABC234" }, Transfer(order, 4) with { Gateway = "Vietcombank" } })
        {
            using HttpResponseMessage response = await SendAsync(transfer); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(OrderStatus.PendingPayment, (await db.Orders.SingleAsync(Token)).Status);
        Assert.Equal(4, await db.Payments.CountAsync(Token)); Assert.Empty(await db.DishStats.ToListAsync(Token));
    }

    [Fact]
    public async Task OnlyOwnerCanConfirmAndConcurrentDuplicateDoesNotDoubleCount()
    {
        Order order = await PlaceAsync();
        using HttpClient manager = await LoginAsync("payment-manager");
        using HttpResponseMessage denied = await manager.PostAsJsonAsync($"/api/v1/owner/orders/{order.Id}/confirm-payment", new { }, Token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using HttpClient other = await LoginAsync("other-payment-owner");
        using HttpResponseMessage scoped = await other.PostAsJsonAsync($"/api/v1/owner/orders/{order.Id}/confirm-payment", new { }, Token);
        Assert.Equal(HttpStatusCode.NotFound, scoped.StatusCode);
        using HttpClient owner = await LoginAsync("payment-owner");
        HttpResponseMessage[] responses = await Task.WhenAll(
            owner.PostAsJsonAsync($"/api/v1/owner/orders/{order.Id}/confirm-payment", new { }, Token), SendAsync(Transfer(order)), SendAsync(Transfer(order)));
        foreach (HttpResponseMessage response in responses) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); response.Dispose(); }
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, (await db.DishStats.SingleAsync(Token)).OrderCount);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(value => value.Type == "order.paid", Token));
        using HttpResponseMessage ledger = await owner.GetAsync("/api/v1/owner/payments", Token);
        Assert.DoesNotContain("rawPayload", await ledger.Content.ReadAsStringAsync(Token), StringComparison.OrdinalIgnoreCase);
    }
}
