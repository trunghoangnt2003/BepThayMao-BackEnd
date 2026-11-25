using System.Diagnostics.Metrics;

namespace Bepthaymao.Infrastructure.Messaging;

/// <summary>
/// Số đo outbox/consumer: tin đã phát, không có route, độ trễ từ lúc ghi outbox tới lúc broker xác nhận, số lần thử lại,
/// tin vào DLQ và vòng phát lỗi. Chỉ mang loại sự kiện và lý do, không mang payload hay dữ liệu khách (T-03-07).
/// </summary>
public static class MessagingMetrics
{
    public const string MeterName = "Bepthaymao.Messaging";
    public const string PublishedInstrument = "bepthaymao.outbox.published";
    public const string UnroutableInstrument = "bepthaymao.outbox.unroutable";
    public const string PublishLagInstrument = "bepthaymao.outbox.publish_lag";
    public const string DispatchFailureInstrument = "bepthaymao.outbox.dispatch_failures";
    public const string RetriedInstrument = "bepthaymao.consumer.retried";
    public const string DeadLetteredInstrument = "bepthaymao.consumer.dead_lettered";

    private static readonly Meter _meter = new(MeterName);
    private static readonly Counter<long> _published = _meter.CreateCounter<long>(PublishedInstrument, "{event}", "Sự kiện outbox đã được broker xác nhận");
    private static readonly Counter<long> _unroutable = _meter.CreateCounter<long>(UnroutableInstrument, "{event}", "Sự kiện outbox không có hàng đợi nhận");
    private static readonly Histogram<double> _publishLag = _meter.CreateHistogram<double>(PublishLagInstrument, "s", "Độ trễ từ ghi outbox tới broker xác nhận");
    private static readonly Counter<long> _dispatchFailures = _meter.CreateCounter<long>(DispatchFailureInstrument, "{batch}", "Vòng phát outbox lỗi, sẽ thử lại");
    private static readonly Counter<long> _retried = _meter.CreateCounter<long>(RetriedInstrument, "{message}", "Tin consumer lỗi và chuyển sang hàng thử lại");
    private static readonly Counter<long> _deadLettered = _meter.CreateCounter<long>(DeadLetteredInstrument, "{message}", "Tin consumer vào hàng chết");

    internal static void Published(string type, double lagSeconds)
    {
        _published.Add(1, new KeyValuePair<string, object?>("type", type));
        _publishLag.Record(Math.Max(0, lagSeconds));
    }

    internal static void Unroutable(string type) => _unroutable.Add(1, new KeyValuePair<string, object?>("type", type));

    public static void DispatchFailed() => _dispatchFailures.Add(1);

    internal static void Retried(int attempt) => _retried.Add(1, new KeyValuePair<string, object?>("attempt", attempt));

    internal static void DeadLettered(string reason) => _deadLettered.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
