using System.Net;
using System.Text.Json;
using Bepthaymao.Api.Realtime;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Orders;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Bepthaymao.Integration.Tests.Payments;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Notifications;

[Collection(SharedInfrastructure.Name)]
public sealed class RealtimeTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await PaymentWorld.CreateAsync(infrastructure);
    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private sealed class RecordingHub<THub> : IHubContext<THub> where THub : Hub
    {
        public List<(string Group, string Method, object? Payload)> Sent { get; } = [];
        public IHubClients Clients => new RecordingClients(Sent);
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class RecordingClients(List<(string Group, string Method, object? Payload)> sent) : IHubClients
        {
            public IClientProxy All => throw new NotSupportedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy Group(string groupName) => new Proxy(sent, groupName);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IClientProxy User(string userId) => throw new NotSupportedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class Proxy(List<(string Group, string Method, object? Payload)> sent, string group) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                sent.Add((group, method, args.Single()));
                return Task.CompletedTask;
            }
        }
    }

    private static byte[] Envelope(Guid eventId, string type, Guid orderId) => JsonSerializer.SerializeToUtf8Bytes(
        new { eventId, type, occurredAt = DateTimeOffset.UtcNow, version = 1, data = new { orderId } }, JsonSerializerOptions.Web);

    private async Task<(RecordingHub<StaffHub> Staff, RecordingHub<OrderHub> Customer)> ProcessAsync(params byte[][] bodies)
    {
        RecordingHub<StaffHub> staff = new();
        RecordingHub<OrderHub> customer = new();
        foreach (byte[] body in bodies)
        {
            using IServiceScope scope = _world.OpenScope();
            await new RealtimeConsumer(scope.ServiceProvider.GetRequiredService<AppDbContext>(), staff, customer, _world.Clock)
                .ProcessAsync(body, 0, Token);
        }
        return (staff, customer);
    }

    [Fact]
    public async Task PaidEventReachesOnlyTheOrdersBranchGroupAndTheOrderGroupWithoutPersonalData()
    {
        Order order = await NotificationWorld.PlacePaidAsync(_world);

        (RecordingHub<StaffHub> staff, RecordingHub<OrderHub> customer) = await ProcessAsync(Envelope(Guid.NewGuid(), "order.paid", order.Id));

        (string staffGroup, string method, object? payload) = Assert.Single(staff.Sent);
        Assert.Equal((StaffHub.BranchGroup(_world.BranchA), "orderChanged"), (staffGroup, method));
        Assert.Equal(OrderHub.OrderGroup(order.Id), Assert.Single(customer.Sent).Group);
        string json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
        Assert.Contains(order.Code, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Test street", json, StringComparison.Ordinal);
        Assert.DoesNotContain(PaymentWorld.Phone, json, StringComparison.Ordinal);
        Assert.DoesNotContain("customerName", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AbandonedEventAlertsStaffOnlyNeverTheCustomer()
    {
        Order order = await NotificationWorld.PlacePaidAsync(_world);

        (RecordingHub<StaffHub> staff, RecordingHub<OrderHub> customer) = await ProcessAsync(Envelope(Guid.NewGuid(), "order.abandoned", order.Id));

        Assert.Single(staff.Sent);
        Assert.Empty(customer.Sent);
    }

    [Fact]
    public async Task DuplicateEventIsPushedOnce()
    {
        Order order = await NotificationWorld.PlacePaidAsync(_world);
        byte[] body = Envelope(Guid.NewGuid(), "order.paid", order.Id);

        (RecordingHub<StaffHub> staff, RecordingHub<OrderHub> customer) = await ProcessAsync(body, body);

        Assert.Single(staff.Sent);
        Assert.Single(customer.Sent);
    }

    [Fact]
    public async Task UnknownOrderAndIrrelevantEventPushNothing()
    {
        Order order = await NotificationWorld.PlacePaidAsync(_world);

        (RecordingHub<StaffHub> staff, RecordingHub<OrderHub> customer) = await ProcessAsync(
            Envelope(Guid.NewGuid(), "order.paid", Guid.NewGuid()),
            Envelope(Guid.NewGuid(), "menu.changed", order.Id));

        Assert.Empty(staff.Sent);
        Assert.Empty(customer.Sent);
    }

    [Fact]
    public async Task MalformedEventIsRejected()
    {
        await Assert.ThrowsAsync<FormatException>(() => ProcessAsync(Envelope(Guid.Empty, "order.paid", Guid.NewGuid())));
    }

    [Fact]
    public async Task StaffHubRequiresStaffWhileOrderHubIsOpenToCustomers()
    {
        using HttpClient anonymous = _world.Factory.CreateClient();
        using HttpClient manager = await _world.LoginAsync("manager-a");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/hubs/staff/negotiate?negotiateVersion=1", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync("/api/hubs/staff/negotiate?negotiateVersion=1", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsync("/api/hubs/order/negotiate?negotiateVersion=1", null, Token)).StatusCode);
    }

    [Fact]
    public async Task AccessTokenInQueryStringWorksOnlyForTheStaffHub()
    {
        using HttpClient manager = await _world.LoginAsync("manager-a");
        string token = manager.DefaultRequestHeaders.Authorization!.Parameter!;
        using HttpClient anonymous = _world.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsync($"/api/hubs/staff/negotiate?negotiateVersion=1&access_token={token}", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/manager/notifications?access_token={token}", Token)).StatusCode);
    }

    [Fact]
    public async Task OrderWatchRejectsUnknownOrderWithoutRevealingWhy()
    {
        OrderHub hub = new(new RejectingAuthorizer()) { Context = new FakeCallerContext() };

        HubException error = await Assert.ThrowsAsync<HubException>(() => hub.Watch("KHONGCO", "0900000000"));

        Assert.Equal("Không tìm thấy đơn.", error.Message);
        await Assert.ThrowsAsync<HubException>(() => hub.Watch(" ", null));
    }

    [Fact]
    public async Task RepeatedFailedWatchesAbortTheConnection()
    {
        FakeCallerContext context = new();
        OrderHub hub = new(new RejectingAuthorizer()) { Context = context };

        for (int attempt = 0; attempt < OrderHub.MaxFailedWatches; attempt++)
            _ = await Assert.ThrowsAsync<HubException>(() => hub.Watch("KHONGCO", "0900000000"));
        Assert.False(context.Aborted);
        _ = await Assert.ThrowsAsync<HubException>(() => hub.Watch("KHONGCO", "0900000001"));

        Assert.True(context.Aborted);
    }

    private sealed class FakeCallerContext : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId => "test-connection";
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    private sealed class RejectingAuthorizer : IQueryHandler<AuthorizeOrderWatchQuery, Guid>
    {
        public Task<Guid> Handle(AuthorizeOrderWatchQuery query, CancellationToken cancellationToken) =>
            throw new Bepthaymao.Application.Abstractions.Errors.NotFoundException();
    }
}
