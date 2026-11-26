using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Bepthaymao.Integration.Tests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Orders;

[Collection(SharedInfrastructure.Name)]
public sealed class OrderStatusEndpointTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await PaymentWorld.CreateAsync(infrastructure);
    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private static string StatusPath(Order order) => $"/api/v1/manager/orders/{order.Id}/status";
    private static string OwnerStatusPath(Order order) => $"/api/v1/owner/orders/{order.Id}/status";

    private async Task<Order> PlacePaidOrderAsync(Guid? branchId = null)
    {
        Order order = await _world.PlaceAsync(branch: branchId);
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order tracked = await db.Orders.SingleAsync(value => value.Id == order.Id, Token);
        DateTimeOffset now = _world.Clock.GetUtcNow();
        Assert.True(tracked.ReceivePayment(tracked.ItemsTotal, now, now));
        await db.SaveChangesAsync(Token);
        return tracked;
    }

    [Fact]
    public async Task ManagerAcceptsPaidOrderInOwnBranchAndRecordsOutbox()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient manager = await _world.LoginAsync("manager-a");

        using HttpResponseMessage response = await manager.PostAsJsonAsync(StatusPath(order), new { to = "Accepted" }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement dto = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("Accepted", dto.GetProperty("status").GetString());

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Accepted, saved.Status);
        Assert.NotNull(saved.AcceptedAt);
        Assert.NotNull(saved.AcceptedAt);

        var outbox = await db.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == "order.status_changed")
            .ToListAsync(Token);
        var message = Assert.Single(outbox);
        using JsonDocument payload = JsonDocument.Parse(message.Payload);
        Assert.Equal("Paid", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal("Accepted", payload.RootElement.GetProperty("to").GetString());
        Assert.Equal(_world.BranchA, payload.RootElement.GetProperty("branchId").GetGuid());
    }

    [Fact]
    public async Task UnknownFieldInRequestBodyReturnsBadRequest()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient manager = await _world.LoginAsync("manager-a");

        using HttpResponseMessage response = await manager.PostAsJsonAsync(StatusPath(order), new { to = "Accepted", foo = 1 }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ManagerAdvancesOrderThroughFullFulfilmentChain()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient manager = await _world.LoginAsync("manager-a");

        foreach (string step in new[] { "Accepted", "Preparing", "Delivering", "Completed" })
        {
            _world.Clock.Advance(TimeSpan.FromMinutes(1));
            using HttpResponseMessage response = await manager.PostAsJsonAsync(StatusPath(order), new { to = step }, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            JsonElement dto = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
            Assert.Equal(step, dto.GetProperty("status").GetString());
        }

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Completed, saved.Status);
        Assert.NotNull(saved.AcceptedAt);
        Assert.NotNull(saved.CompletedAt);

        var outbox = await db.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == "order.status_changed")
            .OrderBy(message => message.OccurredAt)
            .ToListAsync(Token);
        Assert.Equal(4, outbox.Count);
    }

    [Fact]
    public async Task InvalidTransitionsReturnConflictAndLeaveOrderAndOutboxUnchanged()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient manager = await _world.LoginAsync("manager-a");

        // Nhảy bước Paid -> Preparing
        using (HttpResponseMessage jump = await manager.PostAsJsonAsync(StatusPath(order), new { to = "Preparing" }, Token))
            Assert.Equal(HttpStatusCode.Conflict, jump.StatusCode);

        // Chuyển hợp lệ sang Accepted rồi thử lặp lại, đặt Paid, đặt PendingPayment
        using (HttpResponseMessage accept = await manager.PostAsJsonAsync(StatusPath(order), new { to = "Accepted" }, Token))
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        foreach (string invalid in new[] { "Accepted", "Paid", "PendingPayment", "Delivering" })
        {
            using HttpResponseMessage res = await manager.PostAsJsonAsync(StatusPath(order), new { to = invalid }, Token);
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        }

        // Lùi bước từ Preparing -> Accepted
        using (HttpResponseMessage prep = await manager.PostAsJsonAsync(StatusPath(order), new { to = "Preparing" }, Token))
            Assert.Equal(HttpStatusCode.OK, prep.StatusCode);
        using (HttpResponseMessage back = await manager.PostAsJsonAsync(StatusPath(order), new { to = "Accepted" }, Token))
            Assert.Equal(HttpStatusCode.Conflict, back.StatusCode);

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Preparing, saved.Status);
        Assert.Equal(2, await db.OutboxMessages.CountAsync(message => message.Type == "order.status_changed", Token));
    }

    [Fact]
    public async Task ManagerCancelsOrderWithReasonCodeAndValidatesOtherReason()
    {
        Order order1 = await PlacePaidOrderAsync(_world.BranchA);
        Order order2 = await _world.PlaceAsync(branch: _world.BranchA);
        Order order3 = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient manager = await _world.LoginAsync("manager-a");

        // Thiếu reasonCode -> 400
        using (HttpResponseMessage missingCode = await manager.PostAsJsonAsync(StatusPath(order1), new { to = "Cancelled" }, Token))
            Assert.Equal(HttpStatusCode.BadRequest, missingCode.StatusCode);

        // reasonCode = Other nhưng thiếu reason -> 400
        using (HttpResponseMessage missingOtherReason = await manager.PostAsJsonAsync(StatusPath(order1), new { to = "Cancelled", reasonCode = "Other", reason = "   " }, Token))
            Assert.Equal(HttpStatusCode.BadRequest, missingOtherReason.StatusCode);

        // Hủy Paid với OutOfStock -> 200, lưu CancelReasonCode và nhãn tiếng Việt
        using (HttpResponseMessage cancelPaid = await manager.PostAsJsonAsync(StatusPath(order1), new { to = "Cancelled", reasonCode = "OutOfStock" }, Token))
        {
            Assert.Equal(HttpStatusCode.OK, cancelPaid.StatusCode);
            JsonElement dto = await cancelPaid.Content.ReadFromJsonAsync<JsonElement>(Token);
            Assert.Equal("Cancelled", dto.GetProperty("status").GetString());
            Assert.Equal("ByStaff", dto.GetProperty("cancellationKind").GetString());
        }

        // Hủy PendingPayment với Other + reason -> 200
        using (HttpResponseMessage cancelPending = await manager.PostAsJsonAsync(StatusPath(order2), new { to = "Cancelled", reasonCode = "Other", reason = "Quán nghỉ đột xuất" }, Token))
            Assert.Equal(HttpStatusCode.OK, cancelPending.StatusCode);

        // Chuyển order3 sang Preparing -> không hủy được nữa (409)
        using (HttpResponseMessage accept = await manager.PostAsJsonAsync(StatusPath(order3), new { to = "Accepted" }, Token))
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        using (HttpResponseMessage prep = await manager.PostAsJsonAsync(StatusPath(order3), new { to = "Preparing" }, Token))
            Assert.Equal(HttpStatusCode.OK, prep.StatusCode);
        using (HttpResponseMessage cancelPrep = await manager.PostAsJsonAsync(StatusPath(order3), new { to = "Cancelled", reasonCode = "CustomerRequested" }, Token))
            Assert.Equal(HttpStatusCode.Conflict, cancelPrep.StatusCode);

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved1 = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order1.Id, Token);
        Assert.Equal(CancelReasonCode.OutOfStock, saved1.CancelReasonCode);
        Assert.Equal("Hết món", saved1.CancelReason);
        Assert.Equal(CancellationKind.ByStaff, saved1.CancellationKind);

        Order saved2 = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order2.Id, Token);
        Assert.Equal(CancelReasonCode.Other, saved2.CancelReasonCode);
        Assert.Equal("Quán nghỉ đột xuất", saved2.CancelReason);
    }

    [Fact]
    public async Task ManagerOfOtherBranchGetsNotFoundAndOrderUnchanged()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient managerB = await _world.LoginAsync("manager-b");

        using HttpResponseMessage response = await managerB.PostAsJsonAsync(StatusPath(order), new { to = "Accepted" }, Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Paid, saved.Status);
    }

    [Fact]
    public async Task OwnerAcceptsPaidOrderInOwnBranchAndRecordsOutbox()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient ownerA = await _world.LoginAsync("owner-a");

        using HttpResponseMessage response = await ownerA.PostAsJsonAsync(OwnerStatusPath(order), new { to = "Accepted" }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement dto = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("Accepted", dto.GetProperty("status").GetString());

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Accepted, saved.Status);

        var outbox = await db.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == "order.status_changed")
            .ToListAsync(Token);
        var message = Assert.Single(outbox);
        using JsonDocument payload = JsonDocument.Parse(message.Payload);
        Assert.Equal("Paid", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal("Accepted", payload.RootElement.GetProperty("to").GetString());
        Assert.Equal(_world.BranchA, payload.RootElement.GetProperty("branchId").GetGuid());
    }

    [Fact]
    public async Task OtherOwnerGetsNotFoundAndManagerGetsForbiddenOnOwnerStatusRoute()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient ownerZ = await _world.LoginAsync("owner-z");
        using HttpClient managerA = await _world.LoginAsync("manager-a");

        using HttpResponseMessage otherOwnerResponse = await ownerZ.PostAsJsonAsync(OwnerStatusPath(order), new { to = "Accepted" }, Token);
        Assert.Equal(HttpStatusCode.NotFound, otherOwnerResponse.StatusCode);

        using HttpResponseMessage managerResponse = await managerA.PostAsJsonAsync(OwnerStatusPath(order), new { to = "Accepted" }, Token);
        Assert.Equal(HttpStatusCode.Forbidden, managerResponse.StatusCode);

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Paid, saved.Status);
    }

    [Fact]
    public async Task ConcurrentAcceptRequestsYieldOneOkAndOneConflictWithSingleOutboxEvent()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient manager1 = await _world.LoginAsync("manager-a");
        using HttpClient manager2 = await _world.LoginAsync("manager-a");

        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpResponseMessage> first = Task.Run(async () =>
        {
            await gate.Task;
            return await manager1.PostAsJsonAsync(StatusPath(order), new { to = "Accepted" }, Token);
        }, Token);
        Task<HttpResponseMessage> second = Task.Run(async () =>
        {
            await gate.Task;
            return await manager2.PostAsJsonAsync(StatusPath(order), new { to = "Accepted" }, Token);
        }, Token);
        gate.SetResult();

        HttpResponseMessage[] responses = await Task.WhenAll(first, second);
        try
        {
            HttpStatusCode[] statuses = [.. responses.Select(response => response.StatusCode).Order()];
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses) response.Dispose();
        }

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Accepted, saved.Status);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(message => message.Type == "order.status_changed", Token));
    }

    [Fact]
    public async Task ConcurrentManagerPreparingAndOwnerCancelYieldOneOkAndOneConflict()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient manager = await _world.LoginAsync("manager-a");
        using HttpClient owner = await _world.LoginAsync("owner-a");

        // Đưa đơn sang Accepted trước: Preparing và Cancelled loại trừ lẫn nhau ở cả hai thứ tự khóa (Accepted vẫn hủy được theo D-21).
        using (HttpResponseMessage accept = await manager.PostAsJsonAsync(StatusPath(order), new { to = "Accepted" }, Token))
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpResponseMessage> preparingTask = Task.Run(async () =>
        {
            await gate.Task;
            return await manager.PostAsJsonAsync(StatusPath(order), new { to = "Preparing" }, Token);
        }, Token);
        Task<HttpResponseMessage> cancelTask = Task.Run(async () =>
        {
            await gate.Task;
            return await owner.PostAsJsonAsync(OwnerStatusPath(order), new { to = "Cancelled", reasonCode = "OutOfStock" }, Token);
        }, Token);
        gate.SetResult();

        HttpResponseMessage[] responses = await Task.WhenAll(preparingTask, cancelTask);
        HttpStatusCode preparingStatus = responses[0].StatusCode;
        HttpStatusCode cancelStatus = responses[1].StatusCode;
        foreach (HttpResponseMessage response in responses) response.Dispose();

        HttpStatusCode[] statuses = [.. new[] { preparingStatus, cancelStatus }.Order()];
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        if (preparingStatus == HttpStatusCode.OK)
        {
            Assert.Equal(OrderStatus.Preparing, saved.Status);
        }
        else
        {
            Assert.Equal(OrderStatus.Cancelled, saved.Status);
            Assert.Equal(CancelReasonCode.OutOfStock, saved.CancelReasonCode);
        }

        // 1 sự kiện cho Paid -> Accepted + 1 sự kiện cho bước thắng cuộc (Preparing hoặc Cancelled).
        Assert.Equal(2, await db.OutboxMessages.CountAsync(message => message.Type == "order.status_changed", Token));
    }

    [Fact]
    public async Task FailedTransitionAfterLockRollsBackAndLeavesOrderAndOutboxUnchanged()
    {
        Order order = await PlacePaidOrderAsync(_world.BranchA);
        using HttpClient manager = await _world.LoginAsync("manager-a");

        using HttpResponseMessage response = await manager.PostAsJsonAsync(StatusPath(order), new { to = "Preparing" }, Token);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order saved = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == order.Id, Token);
        Assert.Equal(OrderStatus.Paid, saved.Status);
        Assert.Equal(0, await db.OutboxMessages.CountAsync(message => message.Type == "order.status_changed", Token));
    }

    [Fact]
    public async Task ManagerListPinsAbandonedPaidOrdersFirstByPaidRecordedAtAndHidesCancelReasonFromCustomer()
    {
        using HttpClient manager = await _world.LoginAsync("manager-a");
        using HttpClient owner = await _world.LoginAsync("owner-a");

        // X: Paid, abandoned_alerted_at có, paid_recorded_at sớm nhất
        Order orderX = await PlacePaidOrderAsync(_world.BranchA);
        _world.Clock.Advance(TimeSpan.FromSeconds(10));
        // Y: Paid, abandoned_alerted_at có, paid_recorded_at muộn hơn X
        Order orderY = await PlacePaidOrderAsync(_world.BranchA);
        _world.Clock.Advance(TimeSpan.FromSeconds(10));
        // Z: Accepted (tạo mới nhất), có abandoned_alerted_at nhưng không phải Paid nên không bị ghim
        Order orderZ = await PlacePaidOrderAsync(_world.BranchA);
        _world.Clock.Advance(TimeSpan.FromSeconds(10));
        // W: Cancelled để kiểm tra cancelReasonCode/cancelReason với staff vs khách
        Order orderW = await PlacePaidOrderAsync(_world.BranchA);

        using (HttpResponseMessage acceptZ = await manager.PostAsJsonAsync(StatusPath(orderZ), new { to = "Accepted" }, Token))
            Assert.Equal(HttpStatusCode.OK, acceptZ.StatusCode);
        using (HttpResponseMessage cancelW = await manager.PostAsJsonAsync(StatusPath(orderW), new { to = "Cancelled", reasonCode = "OutOfStock" }, Token))
            Assert.Equal(HttpStatusCode.OK, cancelW.StatusCode);

        DateTimeOffset alertedAt = _world.Clock.GetUtcNow();
        using (IServiceScope scope = _world.OpenScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE orders SET abandoned_alerted_at = {alertedAt} WHERE id IN ({orderX.Id}, {orderY.Id}, {orderZ.Id})",
                Token);
        }

        using HttpResponseMessage listResponse = await manager.GetAsync("/api/v1/manager/orders", Token);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        JsonElement[] list = await listResponse.Content.ReadFromJsonAsync<JsonElement[]>(Token) ?? [];
        Guid[] actualOrder = [.. list.Select(item => item.GetProperty("id").GetGuid())];
        // Ghim Paid (X trước Y theo paidRecordedAt tăng dần), rồi các đơn còn lại mới nhất trước (W trước Z)
        Assert.Equal([orderX.Id, orderY.Id, orderW.Id, orderZ.Id], actualOrder);

        using HttpResponseMessage ownerListResponse = await owner.GetAsync("/api/v1/owner/orders", Token);
        Assert.Equal(HttpStatusCode.OK, ownerListResponse.StatusCode);
        JsonElement[] ownerList = await ownerListResponse.Content.ReadFromJsonAsync<JsonElement[]>(Token) ?? [];
        Assert.Equal([orderX.Id, orderY.Id, orderW.Id, orderZ.Id], [.. ownerList.Select(item => item.GetProperty("id").GetGuid())]);

        JsonElement dtoX = list.Single(item => item.GetProperty("id").GetGuid() == orderX.Id);
        Assert.NotEqual(JsonValueKind.Null, dtoX.GetProperty("paidRecordedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, dtoX.GetProperty("abandonedAlertedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, dtoX.GetProperty("acceptedAt").ValueKind);

        JsonElement dtoZ = list.Single(item => item.GetProperty("id").GetGuid() == orderZ.Id);
        Assert.NotEqual(JsonValueKind.Null, dtoZ.GetProperty("acceptedAt").ValueKind);

        JsonElement dtoW = list.Single(item => item.GetProperty("id").GetGuid() == orderW.Id);
        Assert.Equal("OutOfStock", dtoW.GetProperty("cancelReasonCode").GetString());
        Assert.Equal("Hết món", dtoW.GetProperty("cancelReason").GetString());

        // Khách xem GET /orders/{code} không thấy cancelReasonCode/cancelReason
        using HttpClient customer = _world.Factory.CreateClient();
        using HttpResponseMessage trackResponse = await customer.GetAsync($"/api/v1/orders/{orderW.Code}?phone={PaymentWorld.Phone}", Token);
        Assert.Equal(HttpStatusCode.OK, trackResponse.StatusCode);
        JsonElement trackedOrder = (await trackResponse.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("order");
        Assert.Equal(JsonValueKind.Null, trackedOrder.GetProperty("cancelReasonCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, trackedOrder.GetProperty("cancelReason").ValueKind);
    }
}
