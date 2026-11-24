using System.Text.Json;
using Bepthaymao.Domain.Events;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Bepthaymao.Infrastructure.Messaging;

public sealed record EventEnvelope(Guid EventId, string Type, DateTimeOffset OccurredAt, int Version, JsonElement Data);

/// <summary>At-least-once delivery. DB transaction không đợi RabbitMQ; consumer bảo vệ side effect bằng inbox.</summary>
public sealed class OutboxDispatcher(AppDbContext db, IConfiguration configuration, TimeProvider clock)
{
    public const string Exchange = "bepthaymao.events";
    public const string Queue = "inapp.q";
    private static readonly JsonSerializerOptions _json = JsonSerializerOptions.Web;

    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        ConnectionFactory factory = new()
        {
            Uri = new(configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:RabbitMq")),
            AutomaticRecoveryEnabled = true
        };
        await using IConnection connection = await factory.CreateConnectionAsync(cancellationToken);
        await using IChannel channel = await connection.CreateChannelAsync(new(publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true), cancellationToken);
        await DeclareAsync(channel, cancellationToken);
        int count = 0;
        for (int i = 0; i < 20; i++)
        {
            OutboxMessage? message = await ClaimAsync(cancellationToken);
            if (message is null) break;
            using JsonDocument data = JsonDocument.Parse(message.Payload);
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(new EventEnvelope(message.Id, message.Type, message.OccurredAt, 1, data.RootElement), _json);
            await channel.BasicPublishAsync(Exchange, message.Type, mandatory: true,
                new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = message.Id.ToString() }, body, cancellationToken);
            // Awaited publish includes broker confirmation; a crash before this DB update may cause safe redelivery.
            await db.OutboxMessages.Where(value => value.Id == message.Id).ExecuteUpdateAsync(setters =>
                setters.SetProperty(value => value.PublishedAt, clock.GetUtcNow()).SetProperty(value => value.ClaimedUntil, (DateTimeOffset?)null), cancellationToken);
            count++;
        }
        await ConsumeOneAsync(channel, cancellationToken);
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

    public static async Task DeclareAsync(IChannel channel, CancellationToken token)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: token);
        foreach (string queue in new[] { "notifier.q", "realtime.q", "webhook-out.q", Queue })
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
            if (queue == Queue) await channel.QueueBindAsync(queue, Exchange, "payment.*", cancellationToken: token);
            else if (queue == "webhook-out.q")
            {
                foreach (string key in new[] { "order.created", "order.paid", "order.status_changed", "order.expired" })
                    await channel.QueueBindAsync(queue, Exchange, key, cancellationToken: token);
            }
            else
            {
                await channel.QueueBindAsync(queue, Exchange, "order.#", cancellationToken: token);
                await channel.QueueBindAsync(queue, Exchange, "payment.*", cancellationToken: token);
            }
        }
        await channel.QueueDeclareAsync(Queue + ".dlq", durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
        for (int retry = 1; retry <= 5; retry++)
        {
            Dictionary<string, object?> args = new()
            {
                ["x-message-ttl"] = (int)(1000 * Math.Pow(2, retry)),
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = Queue
            };
            await channel.QueueDeclareAsync(Queue + ".retry." + retry, durable: true, exclusive: false, autoDelete: false,
                arguments: args, cancellationToken: token);
        }
    }

    public async Task<bool> ConsumeOneAsync(IChannel channel, CancellationToken token)
    {
        BasicGetResult? delivery = await channel.BasicGetAsync(Queue, autoAck: false, token);
        if (delivery is null) return false;
        try
        {
            await ProcessAsync(delivery.Body, token);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, token);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            int attempt = delivery.BasicProperties.Headers?.TryGetValue("btm-retry", out object? raw) == true && raw is int number ? number : 0;
            int next = attempt + 1;
            BasicProperties props = new()
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = delivery.BasicProperties.MessageId,
                Headers = new Dictionary<string, object?> { ["btm-retry"] = next }
            };
            if (next > 5)
            {
                // Durable alert is committed before ack; stable key prevents duplicate alert after a crash.
                Guid id = Guid.TryParse(delivery.BasicProperties.MessageId, out Guid parsed) ? parsed
                    : new Guid(System.Security.Cryptography.SHA256.HashData(delivery.Body.Span).AsSpan(0, 16));
                await RecordDeadLetterAsync(id, token);
            }
            await channel.BasicPublishAsync("", next > 5 ? Queue + ".dlq" : Queue + ".retry." + next, mandatory: true, props, delivery.Body, token);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, token);
        }
        return true;
    }

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken token)
    {
        EventEnvelope envelope = JsonSerializer.Deserialize<EventEnvelope>(body.Span, _json) ?? throw new JsonException();
        if (envelope.Version != 1 || envelope.EventId == Guid.Empty
            || envelope.Type is not ("payment.late" or "payment.overpaid")) throw new FormatException("Sự kiện không hợp lệ.");
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
                OrderId = envelope.Data.TryGetProperty("orderId", out JsonElement order) ? order.GetGuid() : null,
                BranchId = envelope.Data.TryGetProperty("branchId", out JsonElement branch) ? branch.GetGuid() : null
            });
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
    }

    private async Task RecordDeadLetterAsync(Guid id, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(token);
        string key = "dlq:" + id;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
        if (!await db.InboxMessages.AnyAsync(value => value.Consumer == "inapp.dlq" && value.EventId == id, token))
        {
            db.InboxMessages.Add(new() { Consumer = "inapp.dlq", EventId = id, ProcessedAt = clock.GetUtcNow() });
            db.OperationalAlerts.Add(new() { Id = Guid.CreateVersion7(), Type = "messaging.dead_letter", CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
    }
}
