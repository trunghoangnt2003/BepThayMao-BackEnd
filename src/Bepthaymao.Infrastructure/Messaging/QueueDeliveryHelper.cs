using System.Security.Cryptography;
using System.Text.Json;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Bepthaymao.Infrastructure.Messaging;

/// <summary>Đánh dấu lỗi tạm thời từ dịch vụ ngoài để consumer retry mũ tối đa 5 lần rồi mới đưa vào DLQ.</summary>
public class TransientDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Helper dùng chung cho mọi consumer RabbitMQ: ack, retry mũ 5 lần, DLQ + cảnh báo, và inbox idempotency.</summary>
public static class QueueDeliveryHelper
{
    public static async Task<bool> ConsumeOneAsync(
        AppDbContext db,
        TimeProvider clock,
        IChannel channel,
        string queue,
        Func<ReadOnlyMemory<byte>, int, CancellationToken, Task> handleAsync,
        CancellationToken token)
    {
        BasicGetResult? delivery = await channel.BasicGetAsync(queue, autoAck: false, token);
        if (delivery is null) return false;
        int attempt = delivery.BasicProperties.Headers?.TryGetValue("btm-retry", out object? raw) == true && raw is int number ? number : 0;
        try
        {
            await handleAsync(delivery.Body, attempt, token);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, token);
        }
        catch (Exception exception) when (!token.IsCancellationRequested &&
            exception is JsonException or InvalidOperationException or FormatException or DbUpdateException or Npgsql.NpgsqlException or TransientDeliveryException)
        {
            db.ChangeTracker.Clear();
            bool poison = exception is JsonException or FormatException or InvalidOperationException;
            int next = poison || attempt < 0 || attempt >= MessagingTopology.MaxRetries ? MessagingTopology.MaxRetries + 1 : attempt + 1;
            BasicProperties props = new()
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = delivery.BasicProperties.MessageId,
                Headers = new Dictionary<string, object?> { ["btm-retry"] = next }
            };
            if (next > MessagingTopology.MaxRetries)
            {
                Guid id = Guid.TryParse(delivery.BasicProperties.MessageId, out Guid parsed) ? parsed
                    : new Guid(SHA256.HashData(delivery.Body.Span).AsSpan(0, 16));
                string dlqConsumer = queue.EndsWith(".q", StringComparison.Ordinal) ? queue[..^2] + ".dlq" : queue + ".dlq";
                await RecordAlertAsync(db, clock, id, dlqConsumer, "messaging.dead_letter", token);
            }
            string target = next > MessagingTopology.MaxRetries
                ? MessagingTopology.DeadLetterQueue(queue)
                : MessagingTopology.RetryQueue(queue, next);
            await channel.BasicPublishAsync("", target, mandatory: true, props, delivery.Body, token);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, token);
            if (next > MessagingTopology.MaxRetries) MessagingMetrics.DeadLettered(poison ? "schema" : "retries_exhausted");
            else MessagingMetrics.Retried(next);
        }
        return true;
    }

    public static async Task RecordAlertAsync(AppDbContext db, TimeProvider clock, Guid id, string consumer, string type, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(token);
        string key = consumer + ":" + id;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
        if (!await db.InboxMessages.AnyAsync(value => value.Consumer == consumer && value.EventId == id, token))
        {
            db.InboxMessages.Add(new() { Consumer = consumer, EventId = id, ProcessedAt = clock.GetUtcNow() });
            db.OperationalAlerts.Add(new() { Id = Guid.CreateVersion7(), Type = type, CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
    }
}
