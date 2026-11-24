using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Domain.Payments;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Payments;

public sealed class ManualPaymentTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await PaymentWorld.CreateAsync(infrastructure);
    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private static string ConfirmPath(Order order) => $"/api/v1/owner/orders/{order.Id}/confirm-payment";

    private static async Task<JsonElement[]> ListLedgerAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("rawPayload", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadHash", body, StringComparison.OrdinalIgnoreCase);
        return JsonSerializer.Deserialize<JsonElement[]>(body, JsonSerializerOptions.Web)!;
    }

    [Fact]
    public async Task OwnerConfirmsWithoutAmountAndLeavesAuditPaidEventAndOneStat()
    {
        Order order = await _world.PlaceAsync();
        using HttpClient owner = await _world.LoginAsync("owner-a");
        using HttpResponseMessage response = await owner.PostAsJsonAsync(ConfirmPath(order), new { note = "  Khách đưa tiền mặt  " }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement dto = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("Paid", dto.GetProperty("status").GetString());
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Payment payment = await db.Payments.SingleAsync(Token);
        Assert.Equal("MANUAL", payment.Method);
        Assert.Equal(40000, payment.Amount);
        Assert.Equal("Khách đưa tiền mặt", payment.Note);
        Assert.NotNull(payment.ConfirmedByUserId);
        Assert.Equal(_world.OwnerA, payment.OwnerId);
        PaymentAudit audit = await db.PaymentAudits.SingleAsync(Token);
        Assert.Equal("manual_confirmed", audit.Action);
        Assert.Equal(payment.ConfirmedByUserId, audit.ActorId);
        Assert.Equal(1, (await db.DishStats.SingleAsync(Token)).OrderCount);
        Assert.Equal("MANUAL", JsonDocument.Parse((await db.OutboxMessages.SingleAsync(value => value.Type == "order.paid", Token)).Payload)
            .RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task RepeatedAndConcurrentConfirmationsCountOnce()
    {
        Order order = await _world.PlaceAsync();
        using HttpClient owner = await _world.LoginAsync("owner-a");
        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => owner.PostAsJsonAsync(ConfirmPath(order), new { }, Token)));
        foreach (HttpResponseMessage response in responses) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); response.Dispose(); }
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Payments.CountAsync(Token));
        Assert.Equal(1, await db.PaymentAudits.CountAsync(Token));
        Assert.Equal(1, (await db.DishStats.SingleAsync(Token)).OrderCount);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(value => value.Type == "order.paid", Token));
    }

    [Fact]
    public async Task AnonymousManagerOtherOwnerUnknownOrderAndLongNoteAreRejected()
    {
        Order order = await _world.PlaceAsync();
        using HttpClient anonymous = _world.Factory.CreateClient();
        using HttpResponseMessage unauthenticated = await anonymous.PostAsJsonAsync(ConfirmPath(order), new { }, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using HttpClient manager = await _world.LoginAsync("manager-a");
        using HttpResponseMessage forbidden = await manager.PostAsJsonAsync(ConfirmPath(order), new { }, Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using HttpClient other = await _world.LoginAsync("owner-z");
        using HttpResponseMessage crossOwner = await other.PostAsJsonAsync(ConfirmPath(order), new { }, Token);
        Assert.Equal(HttpStatusCode.NotFound, crossOwner.StatusCode);
        using HttpClient owner = await _world.LoginAsync("owner-a");
        using HttpResponseMessage unknown = await owner.PostAsJsonAsync($"/api/v1/owner/orders/{Guid.NewGuid()}/confirm-payment", new { }, Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using HttpResponseMessage tooLong = await owner.PostAsJsonAsync(ConfirmPath(order), new { note = new string('x', 501) }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using HttpResponseMessage amount = await owner.PostAsJsonAsync(ConfirmPath(order), new { amount = 1 }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, amount.StatusCode);
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(OrderStatus.PendingPayment, (await db.Orders.SingleAsync(Token)).Status);
        Assert.Equal(0, await db.Payments.CountAsync(Token));
    }

    [Fact]
    public async Task ConfirmingAnActivelyCancelledOrderIsConflictAndChangesNothing()
    {
        Order order = await _world.PlaceAsync();
        using (IServiceScope scope = _world.OpenScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Orders.SingleAsync(value => value.Id == order.Id, Token)).CancelByCustomer(_world.Clock.GetUtcNow());
            await db.SaveChangesAsync(Token);
        }
        using HttpClient owner = await _world.LoginAsync("owner-a");
        using HttpResponseMessage response = await owner.PostAsJsonAsync(ConfirmPath(order), new { }, Token);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using IServiceScope check = _world.OpenScope();
        AppDbContext verify = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(OrderStatus.Cancelled, (await verify.Orders.SingleAsync(Token)).Status);
        Assert.Equal(0, await verify.Payments.CountAsync(Token));
        Assert.Empty(await verify.DishStats.ToListAsync(Token));
        Assert.Equal(0, await verify.OutboxMessages.CountAsync(value => value.Type == "order.paid", Token));
    }

    [Fact]
    public async Task OwnerCanConfirmAnExpiredOrderBecauseExpiryIsNotAnActiveCancellation()
    {
        Order order = await _world.PlaceAsync(minutesAgo: 16);
        await _world.ExpireAsync(order);
        using HttpClient owner = await _world.LoginAsync("owner-a");
        using HttpResponseMessage response = await owner.PostAsJsonAsync(ConfirmPath(order), new { }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order paid = await db.Orders.SingleAsync(Token);
        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.True(paid.RestoredFromExpiry);
        Assert.Null(paid.CancellationKind);
        Assert.Equal(1, (await db.DishStats.SingleAsync(Token)).OrderCount);
    }

    [Fact]
    public async Task RealMoneyAfterManualConfirmationIsLedgeredButNeverCountedTwice()
    {
        Order order = await _world.PlaceAsync();
        using HttpClient owner = await _world.LoginAsync("owner-a");
        using HttpResponseMessage confirm = await owner.PostAsJsonAsync(ConfirmPath(order), new { }, Token);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        using HttpResponseMessage transfer = await _world.SendAsync(_world.Transfer(order, 7));
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Payment[] ledger = await db.Payments.OrderBy(value => value.ReceivedAt).ToArrayAsync(Token);
        Assert.Equal(["MANUAL", "QR_SEPAY"], ledger.Select(value => value.Method).ToArray());
        Assert.Equal("already_paid", ledger[1].Reason);
        Assert.True(ledger[1].ActionRequired);
        Assert.Equal(1, (await db.DishStats.SingleAsync(Token)).OrderCount);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(value => value.Type == "order.paid", Token));
        Assert.Equal(0, await db.OutboxMessages.CountAsync(value => value.Type == "payment.late", Token));
    }

    [Fact]
    public async Task LedgerIsPaginatedScopedToBranchAndNeverExposesRawPayload()
    {
        Order orderA = await _world.PlaceAsync(_world.BranchA);
        Order orderB = await _world.PlaceAsync(_world.BranchB, account: PaymentWorld.UniqueAccount);
        (await _world.SendAsync(_world.Transfer(orderA, 1))).Dispose();
        (await _world.SendAsync(_world.Transfer(orderB, 2, account: PaymentWorld.UniqueAccount))).Dispose();
        using HttpClient owner = await _world.LoginAsync("owner-a");
        using HttpClient managerA = await _world.LoginAsync("manager-a");
        using HttpClient managerB = await _world.LoginAsync("manager-b");
        using HttpClient ownerZ = await _world.LoginAsync("owner-z");
        Assert.Equal(2, (await ListLedgerAsync(owner, "/api/v1/owner/payments")).Length);
        JsonElement[] forA = await ListLedgerAsync(managerA, "/api/v1/manager/payments");
        JsonElement only = Assert.Single(forA);
        Assert.Equal(orderA.Code, only.GetProperty("orderCode").GetString());
        Assert.Equal(_world.BranchA, only.GetProperty("branchId").GetGuid());
        Assert.Equal(orderB.Code, Assert.Single(await ListLedgerAsync(managerB, "/api/v1/manager/payments")).GetProperty("orderCode").GetString());
        Assert.Empty(await ListLedgerAsync(ownerZ, "/api/v1/owner/payments"));
        Assert.Empty(await ListLedgerAsync(owner, "/api/v1/owner/payments?page=2"));
        using HttpResponseMessage managerOnOwnerRoute = await managerA.GetAsync("/api/v1/owner/payments", Token);
        Assert.Equal(HttpStatusCode.Forbidden, managerOnOwnerRoute.StatusCode);
        using HttpResponseMessage ownerOnManagerRoute = await owner.GetAsync("/api/v1/manager/payments", Token);
        Assert.Equal(HttpStatusCode.Forbidden, ownerOnManagerRoute.StatusCode);
    }

    [Fact]
    public async Task UnmatchedTransfersAreScopedOnlyWhenTheAccountMapsToExactlyOneOwnerOrBranch()
    {
        (await _world.SendAsync(_world.Unmatched(1, PaymentWorld.UniqueAccount))).Dispose();
        (await _world.SendAsync(_world.Unmatched(2, PaymentWorld.SameOwnerAccount))).Dispose();
        (await _world.SendAsync(_world.Unmatched(3, PaymentWorld.SharedAccount))).Dispose();
        (await _world.SendAsync(_world.Unmatched(4, "000000"))).Dispose();
        using HttpClient owner = await _world.LoginAsync("owner-a");
        using HttpClient ownerZ = await _world.LoginAsync("owner-z");
        using HttpClient managerA = await _world.LoginAsync("manager-a");
        using HttpClient managerB = await _world.LoginAsync("manager-b");
        JsonElement[] ownerRows = await ListLedgerAsync(owner, "/api/v1/owner/payments");
        Assert.Equal([1L, 2L], ownerRows.Select(value => value.GetProperty("transactionId").GetInt64()).Order().ToArray());
        Assert.Equal(_world.BranchB, ownerRows.Single(value => value.GetProperty("transactionId").GetInt64() == 1).GetProperty("branchId").GetGuid());
        Assert.Equal(JsonValueKind.Null, ownerRows.Single(value => value.GetProperty("transactionId").GetInt64() == 2).GetProperty("branchId").ValueKind);
        Assert.Empty(await ListLedgerAsync(ownerZ, "/api/v1/owner/payments"));
        Assert.Empty(await ListLedgerAsync(managerA, "/api/v1/manager/payments"));
        Assert.Equal(1L, Assert.Single(await ListLedgerAsync(managerB, "/api/v1/manager/payments")).GetProperty("transactionId").GetInt64());
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await db.Payments.CountAsync(Token));
        Assert.All(await db.Payments.ToListAsync(Token), value => Assert.True(value.ActionRequired));
    }

    [Fact]
    public async Task CustomerCancelRacingTheWebhookNeverLeavesPaidAndCancelledMixed()
    {
        Order order = await _world.PlaceAsync();
        using HttpClient customer = _world.Factory.CreateClient();
        Task<HttpResponseMessage> cancel = customer.PostAsJsonAsync($"/api/v1/orders/{order.Code}/cancel", new { code = order.Code, phone = PaymentWorld.Phone }, Token);
        Task<HttpResponseMessage> pay = _world.SendAsync(_world.Transfer(order));
        using HttpResponseMessage cancelled = await cancel;
        using HttpResponseMessage paid = await pay;
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Contains(cancelled.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order final = await db.Orders.SingleAsync(Token);
        long stat = await db.DishStats.Select(value => value.OrderCount).SingleOrDefaultAsync(Token);
        Assert.Equal(1, await db.Payments.CountAsync(Token));
        if (final.Status == OrderStatus.Paid)
        {
            Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);
            Assert.Equal(1, stat);
            Assert.Equal(1, await db.OutboxMessages.CountAsync(value => value.Type == "order.paid", Token));
        }
        else
        {
            Assert.Equal(OrderStatus.Cancelled, final.Status);
            Assert.Equal(CancellationKind.ByCustomer, final.CancellationKind);
            Assert.Equal(0, stat);
            Assert.Equal(0, await db.OutboxMessages.CountAsync(value => value.Type == "order.paid", Token));
            Assert.True((await db.Payments.SingleAsync(Token)).ActionRequired);
        }
    }
}
