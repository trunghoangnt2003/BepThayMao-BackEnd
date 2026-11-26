using System.Text.Json;
using Bepthaymao.Domain.Events;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Bepthaymao.Infrastructure.Messaging;

public sealed record EventEnvelope(Guid EventId, string Type, DateTimeOffset OccurredAt, int Version, JsonElement Data);

/// <summary>At-least-once delivery. DB transaction không đợi RabbitMQ; consumer bảo vệ side effect bằng inbox.</summary>
public sealed class OutboxDispatcher(AppDbContext db, RabbitBroker broker, TimeProvider clock)
{
    public const string Exchange = "bepthaymao.events";
    public const string Queue = "inapp.q";
    private static readonly JsonSerializerOptions _json = JsonSerializerOptions.Web;

    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        await using IChannel channel = await broker.CreateConfirmChannelAsync(cancellationToken);
        int count = 0;
        for (int i = 0; i < 20; i++)
        {
            OutboxMessage? message = await ClaimAsync(cancellationToken);
            if (message is null) break;
            if (message.Attempts >= 5)
                await QueueDeliveryHelper.RecordAlertAsync(db, clock, message.Id, "outbox.stuck", "messaging.outbox_stuck", cancellationToken);
            try
            {
                using JsonDocument data = JsonDocument.Parse(message.Payload);
                byte[] body = JsonSerializer.SerializeToUtf8Bytes(new EventEnvelope(message.Id, message.Type, message.OccurredAt, 1, data.RootElement), _json);
                // Deadline ngắn hơn lease 30 giây; worker khác chỉ lấy lại khi lease hết hạn.
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await channel.BasicPublishAsync(Exchange, message.Type, mandatory: true,
                    new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = message.Id.ToString() }, body, deadline.Token);
            }
            catch (PublishException exception) when (exception.IsReturn)
            {
                MessagingMetrics.Unroutable(message.Type);
                continue;
            }
            catch (JsonException)
            {
                await QueueDeliveryHelper.RecordAlertAsync(db, clock, message.Id, "outbox.stuck", "messaging.outbox_stuck", cancellationToken);
                continue;
            }
            // Awaited publish includes broker confirmation; a crash before this DB update may cause safe redelivery.
            await db.OutboxMessages.Where(value => value.Id == message.Id && value.ClaimedUntil == message.ClaimedUntil && value.Attempts == message.Attempts).ExecuteUpdateAsync(setters =>
                setters.SetProperty(value => value.PublishedAt, clock.GetUtcNow()).SetProperty(value => value.ClaimedUntil, (DateTimeOffset?)null), cancellationToken);
            count++;
            MessagingMetrics.Published(message.Type, (clock.GetUtcNow() - message.OccurredAt).TotalSeconds);
        }
        for (int i = 0; i < 20 && await ConsumeOneAsync(channel, cancellationToken); i++) { }
        return count;
    }

    private async Task<OutboxMessage?> ClaimAsync(CancellationToken token)
    {
        db.ChangeTracker.Clear();
        DateTimeOffset now = clock.GetUtcNow();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(token);
        OutboxMessage? message = await db.OutboxMessages.FromSql($"SELECT * FROM outbox_messages WHERE published_at IS NULL AND (claimed_until IS NULL OR claimed_until < {now}) ORDER BY occurred_at LIMIT 1 FOR UPDATE SKIP LOCKED")
            .SingleOrDefaultAsync(token);
        if (message is not null)
        {
            message.ClaimedUntil = now.AddSeconds(30);
            message.Attempts++;
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
        return message;
    }

    public static Task DeclareAsync(IChannel channel, CancellationToken token) => MessagingTopology.DeclareAsync(channel, token);

    public Task<bool> ConsumeOneAsync(IChannel channel, CancellationToken token) =>
        QueueDeliveryHelper.ConsumeOneAsync(db, clock, channel, Queue, (body, _, ct) => ProcessAsync(body, ct), token);

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken token)
    {
        EventEnvelope envelope = JsonSerializer.Deserialize<EventEnvelope>(body.Span, _json) ?? throw new JsonException();
        if (envelope.Version != 1 || envelope.EventId == Guid.Empty
            || envelope.Type is not ("payment.late" or "payment.overpaid" or "order.abandoned")
            || envelope.Data.ValueKind != JsonValueKind.Object
            || !envelope.Data.TryGetProperty("orderId", out JsonElement orderId) || !orderId.TryGetGuid(out Guid orderGuid) || orderGuid == Guid.Empty
            || !envelope.Data.TryGetProperty("branchId", out JsonElement branchId) || !branchId.TryGetGuid(out Guid branchGuid) || branchGuid == Guid.Empty)
            throw new FormatException("Sự kiện không hợp lệ.");
        db.ChangeTracker.Clear();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(token);
        string key = "inapp:" + envelope.EventId;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
        if (!await db.InboxMessages.AnyAsync(value => value.Consumer == Queue && value.EventId == envelope.EventId, token))
        {
            db.InboxMessages.Add(new() { Consumer = Queue, EventId = envelope.EventId, ProcessedAt = clock.GetUtcNow() });
            db.OperationalAlerts.Add(new()
            {
                Id = Guid.CreateVersion7(),
                Type = envelope.Type,
                CreatedAt = clock.GetUtcNow(),
                OrderId = orderGuid,
                BranchId = branchGuid
            });
            if (envelope.Type == "order.abandoned")
            {
                string? orderCode = await db.Orders.Where(o => o.Id == orderGuid).Select(o => o.Code).SingleOrDefaultAsync(token);
                int? waited = envelope.Data.TryGetProperty("waitedMinutes", out JsonElement waitedEl) && waitedEl.TryGetInt32(out int w) ? w : null;
                db.Notifications.Add(new()
                {
                    Id = Guid.CreateVersion7(),
                    Type = NotificationType.AbandonedAlert,
                    Channel = NotificationChannel.InApp,
                    Status = NotificationStatus.Sent,
                    Attempts = 1,
                    BranchId = branchGuid,
                    OrderId = orderGuid,
                    OrderCode = orderCode,
                    WaitedMinutes = waited,
                    CreatedAt = clock.GetUtcNow(),
                    SentAt = clock.GetUtcNow()
                });
            }
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
    }
}
