using System.Text.Json;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Events;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Messaging;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Bepthaymao.Infrastructure.Notifications;

/// <summary>Tiêu thụ hàng đợi <c>notifier.q</c>: gửi tin Telegram cho group cơ sở, ghi sổ <c>notifications</c> và bảo vệ idempotent qua <c>inbox_messages</c>.</summary>
public sealed class NotifierConsumer(
    AppDbContext db,
    ITelegramSender telegramSender,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions _json = JsonSerializerOptions.Web;

    public Task<bool> ConsumeOneAsync(IChannel channel, CancellationToken token) =>
        QueueDeliveryHelper.ConsumeOneAsync(db, clock, channel, MessagingTopology.NotifierQueue, ProcessAsync, token);

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, int attempt, CancellationToken token)
    {
        EventEnvelope envelope = JsonSerializer.Deserialize<EventEnvelope>(body.Span, _json) ?? throw new JsonException();
        if (envelope.Version != 1 || envelope.EventId == Guid.Empty || envelope.Data.ValueKind != JsonValueKind.Object)
            throw new FormatException("Sự kiện thông báo không hợp lệ.");

        if (envelope.Type is not ("order.paid" or "order.status_changed" or "order.abandoned"))
        {
            await MarkInboxOnlyAsync(envelope.EventId, token);
            return;
        }

        if (envelope.Type == "order.status_changed")
        {
            string? from = envelope.Data.TryGetProperty("from", out JsonElement fromEl) ? fromEl.GetString() : null;
            string? to = envelope.Data.TryGetProperty("to", out JsonElement toEl) ? toEl.GetString() : null;
            if (to != nameof(OrderStatus.Cancelled) || from is not (nameof(OrderStatus.Paid) or nameof(OrderStatus.Accepted)))
            {
                await MarkInboxOnlyAsync(envelope.EventId, token);
                return;
            }
        }

        if (!envelope.Data.TryGetProperty("orderId", out JsonElement orderIdEl) || !orderIdEl.TryGetGuid(out Guid orderId) || orderId == Guid.Empty)
            throw new FormatException("Thiếu orderId trong sự kiện.");

        db.ChangeTracker.Clear();
        if (await db.InboxMessages.AnyAsync(v => v.Consumer == MessagingTopology.NotifierQueue && v.EventId == envelope.EventId, token))
            return;

        Order? order = await db.Orders.Include(o => o.Items).AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, token);
        if (order is null)
        {
            await MarkInboxOnlyAsync(envelope.EventId, token);
            return;
        }

        Branch? branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == order.BranchId, token);
        if (branch is null)
        {
            await MarkInboxOnlyAsync(envelope.EventId, token);
            return;
        }

        (NotificationType type, string html, int? waitedMinutes) = BuildMessage(envelope, order, branch);
        DateTimeOffset now = clock.GetUtcNow();

        if (branch.TelegramChatId is not long chatId)
        {
            await RecordResultAsync(envelope.EventId, new Notification
            {
                Id = Guid.CreateVersion7(),
                Type = type,
                Channel = NotificationChannel.Telegram,
                Status = NotificationStatus.Skipped,
                Attempts = attempt + 1,
                BranchId = branch.Id,
                OrderId = order.Id,
                OrderCode = order.Code,
                WaitedMinutes = waitedMinutes,
                CreatedAt = now
            }, extraAlert: null, token);
            return;
        }

        try
        {
            await telegramSender.SendHtmlAsync(chatId, html, token);
            await RecordResultAsync(envelope.EventId, new Notification
            {
                Id = Guid.CreateVersion7(),
                Type = type,
                Channel = NotificationChannel.Telegram,
                Status = NotificationStatus.Sent,
                Attempts = attempt + 1,
                BranchId = branch.Id,
                OrderId = order.Id,
                OrderCode = order.Code,
                WaitedMinutes = waitedMinutes,
                CreatedAt = now,
                SentAt = clock.GetUtcNow()
            }, extraAlert: null, token);
        }
        catch (TelegramPermanentException ex)
        {
            string truncatedError = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
            Notification failed = new()
            {
                Id = Guid.CreateVersion7(),
                Type = type,
                Channel = NotificationChannel.Telegram,
                Status = NotificationStatus.Failed,
                Attempts = attempt + 1,
                BranchId = branch.Id,
                OrderId = order.Id,
                OrderCode = order.Code,
                WaitedMinutes = waitedMinutes,
                LastError = truncatedError,
                CreatedAt = now
            };
            Notification misconfigured = new()
            {
                Id = Guid.CreateVersion7(),
                Type = NotificationType.TelegramMisconfigured,
                Channel = NotificationChannel.InApp,
                Status = NotificationStatus.Sent,
                Attempts = 1,
                BranchId = branch.Id,
                OrderId = order.Id,
                OrderCode = order.Code,
                LastError = truncatedError,
                CreatedAt = now,
                SentAt = now
            };
            await RecordResultAsync(envelope.EventId, failed, misconfigured, token);
        }
    }

    private static (NotificationType Type, string Html, int? WaitedMinutes) BuildMessage(EventEnvelope envelope, Order order, Branch branch)
    {
        switch (envelope.Type)
        {
            case "order.paid":
                bool restored = envelope.Data.TryGetProperty("restoredFromExpiry", out JsonElement rEl) && rEl.ValueKind == JsonValueKind.True;
                bool late = envelope.Data.TryGetProperty("latePayment", out JsonElement lEl) && lEl.ValueKind == JsonValueKind.True;
                return (NotificationType.OrderNew, TelegramMessageFormatter.FormatOrderPaid(order, branch, restored, late), null);

            case "order.status_changed":
                string from = envelope.Data.TryGetProperty("from", out JsonElement fromEl) ? fromEl.GetString() ?? "" : "";
                return (NotificationType.OrderCancelled, TelegramMessageFormatter.FormatOrderCancelled(order, branch, from), null);

            case "order.abandoned":
                int waited = envelope.Data.TryGetProperty("waitedMinutes", out JsonElement wEl) && wEl.TryGetInt32(out int m) ? m : 10;
                return (NotificationType.AbandonedAlert, TelegramMessageFormatter.FormatOrderAbandoned(order, branch, waited), waited);

            default:
                throw new FormatException("Loại sự kiện không được hỗ trợ.");
        }
    }

    private async Task MarkInboxOnlyAsync(Guid eventId, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(token);
        string key = "notifier:" + eventId;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
        if (!await db.InboxMessages.AnyAsync(v => v.Consumer == MessagingTopology.NotifierQueue && v.EventId == eventId, token))
        {
            db.InboxMessages.Add(new() { Consumer = MessagingTopology.NotifierQueue, EventId = eventId, ProcessedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
    }

    private async Task RecordResultAsync(Guid eventId, Notification main, Notification? extraAlert, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(token);
        string key = "notifier:" + eventId;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
        if (!await db.InboxMessages.AnyAsync(v => v.Consumer == MessagingTopology.NotifierQueue && v.EventId == eventId, token))
        {
            db.InboxMessages.Add(new() { Consumer = MessagingTopology.NotifierQueue, EventId = eventId, ProcessedAt = clock.GetUtcNow() });
            db.Notifications.Add(main);
            if (extraAlert is not null) db.Notifications.Add(extraAlert);
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
    }
}
