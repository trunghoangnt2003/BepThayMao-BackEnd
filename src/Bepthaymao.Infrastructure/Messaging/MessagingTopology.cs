using RabbitMQ.Client;

namespace Bepthaymao.Infrastructure.Messaging;

/// <summary>
/// Topology RabbitMQ theo <c>contracts/events.md</c>: exchange topic bền <c>bepthaymao.events</c>, năm hàng đợi theo consumer,
/// mỗi hàng có <c>*.retry.1..5</c> (trễ 2/4/8/16/32 giây rồi quay lại hàng chính) và <c>*.dlq</c>.
/// Hàng đợi của consumer tương lai (notifier, realtime, webhook ra, report) được khai báo sẵn để sự kiện không mất khi chưa có consumer.
/// </summary>
public static class MessagingTopology
{
    public const string Exchange = "bepthaymao.events";
    public const string InAppQueue = "inapp.q";
    public const string NotifierQueue = "notifier.q";
    public const string RealtimeQueue = "realtime.q";
    public const int MaxRetries = 5;

    private static readonly string[] _publicOrderEvents = ["order.created", "order.paid", "order.status_changed", "order.expired"];

    /// <summary>Hàng đợi và các routing key gắn vào, một nguồn duy nhất cho khai báo và test.</summary>
    public static IReadOnlyDictionary<string, string[]> Bindings { get; } = new Dictionary<string, string[]>
    {
        [InAppQueue] = ["payment.late", "payment.overpaid", "order.abandoned"],
        [NotifierQueue] = ["order.paid", "order.status_changed", "order.abandoned"],
        [RealtimeQueue] = ["order.created", "order.paid", "order.status_changed", "order.expired", "order.abandoned", "payment.overpaid"],
        ["webhook-out.q"] = _publicOrderEvents,
        ["report.q"] = ["report.export_requested"],
    };

    public static string RetryQueue(string queue, int attempt) => queue + ".retry." + attempt;

    public static string DeadLetterQueue(string queue) => queue + ".dlq";

    public static async Task DeclareAsync(IChannel channel, CancellationToken token)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: token);
        foreach ((string queue, string[] keys) in Bindings)
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
            foreach (string key in keys) await channel.QueueBindAsync(queue, Exchange, key, cancellationToken: token);
            await channel.QueueDeclareAsync(DeadLetterQueue(queue), durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                // Hết TTL tin được dead-letter qua exchange mặc định về lại hàng chính, không vòng requeue nóng.
                Dictionary<string, object?> arguments = new()
                {
                    ["x-message-ttl"] = (int)(1000 * Math.Pow(2, attempt)),
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = queue
                };
                await channel.QueueDeclareAsync(RetryQueue(queue, attempt), durable: true, exclusive: false, autoDelete: false,
                    arguments: arguments, cancellationToken: token);
            }
        }
    }
}
