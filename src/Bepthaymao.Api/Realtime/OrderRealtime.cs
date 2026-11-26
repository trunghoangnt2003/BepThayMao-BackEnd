using System.Text.Json;
using Bepthaymao.Api.Hosting;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Orders;
using Bepthaymao.Infrastructure.Messaging;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Bepthaymao.Api.Realtime;

/// <summary>Sự kiện tối thiểu đẩy qua SignalR; không chứa tên, SĐT, địa chỉ khách hay chat_id.</summary>
public sealed record OrderChangedEvent(Guid OrderId, string Code, string Status, DateTimeOffset OccurredAt, string Type);

/// <summary>Hub nhân viên (/api/hubs/staff): server tự gán group cơ sở theo claim, client không tự chọn branchId.</summary>
[Authorize(Policy = PolicyNames.StaffOnly)]
public sealed class StaffHub(AppDbContext db) : Hub
{
    public static string BranchGroup(Guid branchId) => $"branch:{branchId}";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole("BranchManager") == true
            && Guid.TryParse(Context.User.FindFirst("branch_id")?.Value, out Guid branchId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, BranchGroup(branchId), Context.ConnectionAborted);
            await base.OnConnectedAsync();
            return;
        }

        if (Context.User?.IsInRole("Owner") == true
            && Guid.TryParse(Context.User.FindFirst("owner_id")?.Value, out Guid ownerId))
        {
            List<Guid> branchIds = await db.Branches.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(b => b.OwnerId == ownerId)
                .Select(b => b.Id)
                .ToListAsync(Context.ConnectionAborted);
            foreach (Guid id in branchIds)
                await Groups.AddToGroupAsync(Context.ConnectionId, BranchGroup(id), Context.ConnectionAborted);
            await base.OnConnectedAsync();
            return;
        }

        Context.Abort();
    }
}

/// <summary>Hub theo dõi đơn của khách (/api/hubs/order): ẩn danh, kiểm quyền qua cookie btm_don hoặc mã đơn + SĐT.</summary>
[AllowAnonymous]
public sealed class OrderHub(IQueryHandler<AuthorizeOrderWatchQuery, Guid> authorizer) : Hub
{
    public static string OrderGroup(Guid orderId) => $"order:{orderId}";

    /// <summary>Số lần Watch sai tối đa trên một kết nối; vượt ngưỡng thì ngắt, chặn đoán SĐT theo mã đơn qua một socket (REST đã có rate limit).</summary>
    public const int MaxFailedWatches = 5;
    private const string _failuresKey = "watch-failures";

    public async Task Watch(string code, string? phone = null)
    {
        if (Context.Items.TryGetValue(_failuresKey, out object? current) && current is int failures && failures >= MaxFailedWatches)
        {
            Context.Abort();
            throw new HubException("Không tìm thấy đơn.");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new NotFoundException();

            HttpRequest? request = Context.GetHttpContext()?.Request;
            IReadOnlyList<string> tokens = request is null ? [] : OrderAccessCookie.Read(request);
            Guid orderId = await authorizer.Handle(new(code, phone, tokens), Context.ConnectionAborted);
            await Groups.AddToGroupAsync(Context.ConnectionId, OrderGroup(orderId), Context.ConnectionAborted);
        }
        catch (NotFoundException)
        {
            Context.Items[_failuresKey] = (Context.Items.TryGetValue(_failuresKey, out object? previous) && previous is int count ? count : 0) + 1;
            throw new HubException("Không tìm thấy đơn.");
        }
    }
}

/// <summary>Tiêu thụ hàng đợi <c>realtime.q</c> trong tiến trình Api và phát sự kiện <c>orderChanged</c> tới StaffHub/OrderHub.</summary>
public sealed class RealtimeConsumer(
    AppDbContext db,
    IHubContext<StaffHub> staffHub,
    IHubContext<OrderHub> orderHub,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions _json = JsonSerializerOptions.Web;

    public Task<bool> ConsumeOneAsync(IChannel channel, CancellationToken token) =>
        QueueDeliveryHelper.ConsumeOneAsync(db, clock, channel, MessagingTopology.RealtimeQueue, ProcessAsync, token);

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, int _, CancellationToken token)
    {
        EventEnvelope envelope = JsonSerializer.Deserialize<EventEnvelope>(body.Span, _json) ?? throw new JsonException();
        if (envelope.Version != 1 || envelope.EventId == Guid.Empty || envelope.Data.ValueKind != JsonValueKind.Object)
            throw new FormatException("Sự kiện realtime không hợp lệ.");

        if (envelope.Type is not ("order.created" or "order.paid" or "order.status_changed" or "order.expired" or "order.abandoned" or "payment.overpaid"))
        {
            await MarkInboxOnlyAsync(envelope.EventId, token);
            return;
        }

        if (!envelope.Data.TryGetProperty("orderId", out JsonElement orderIdEl) || !orderIdEl.TryGetGuid(out Guid orderId) || orderId == Guid.Empty)
            throw new FormatException("Thiếu orderId trong sự kiện realtime.");

        db.ChangeTracker.Clear();
        if (await db.InboxMessages.AnyAsync(v => v.Consumer == MessagingTopology.RealtimeQueue && v.EventId == envelope.EventId, token))
            return;

        var orderInfo = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.Code, o.BranchId, o.Status })
            .SingleOrDefaultAsync(token);

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(token);
        string lockKey = "realtime:" + envelope.EventId;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", token);
        bool inserted = false;
        if (!await db.InboxMessages.AnyAsync(v => v.Consumer == MessagingTopology.RealtimeQueue && v.EventId == envelope.EventId, token))
        {
            db.InboxMessages.Add(new()
            {
                Consumer = MessagingTopology.RealtimeQueue,
                EventId = envelope.EventId,
                ProcessedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync(token);
            inserted = true;
        }
        await tx.CommitAsync(token);

        if (!inserted || orderInfo is null) return;

        OrderChangedEvent payload = new(orderInfo.Id, orderInfo.Code, orderInfo.Status.ToString(), envelope.OccurredAt, envelope.Type);
        await staffHub.Clients.Group(StaffHub.BranchGroup(orderInfo.BranchId)).SendAsync("orderChanged", payload, token);

        if (envelope.Type is "order.created" or "order.paid" or "order.status_changed" or "order.expired")
            await orderHub.Clients.Group(OrderHub.OrderGroup(orderInfo.Id)).SendAsync("orderChanged", payload, token);
    }

    private async Task MarkInboxOnlyAsync(Guid eventId, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(token);
        string lockKey = "realtime:" + eventId;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", token);
        if (!await db.InboxMessages.AnyAsync(v => v.Consumer == MessagingTopology.RealtimeQueue && v.EventId == eventId, token))
        {
            db.InboxMessages.Add(new()
            {
                Consumer = MessagingTopology.RealtimeQueue,
                EventId = eventId,
                ProcessedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
    }
}

/// <summary>Worker nền trong tiến trình Api đọc hàng đợi <c>realtime.q</c> (D-22).</summary>
internal sealed partial class RealtimeWorker(IServiceScopeFactory scopes, ILogger<RealtimeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (BuildTimeDocs.IsActive) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                RabbitBroker broker = scope.ServiceProvider.GetRequiredService<RabbitBroker>();
                RealtimeConsumer consumer = scope.ServiceProvider.GetRequiredService<RealtimeConsumer>();
                await using IChannel channel = await broker.CreateConfirmChannelAsync(stoppingToken);
                for (int i = 0; i < 20 && await consumer.ConsumeOneAsync(channel, stoppingToken); i++) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                MessagingMetrics.DispatchFailed();
                LogConsumeFailure(logger, exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    [LoggerMessage(LogLevel.Error, "Realtime consumer batch failed ({FailureType}); pending events will be retried")]
    private static partial void LogConsumeFailure(ILogger logger, string failureType);
}
