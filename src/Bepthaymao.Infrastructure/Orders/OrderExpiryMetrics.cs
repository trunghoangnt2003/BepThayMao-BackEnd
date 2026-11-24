using System.Diagnostics.Metrics;

namespace Bepthaymao.Infrastructure.Orders;

/// <summary>
/// Số đo của job hết hạn đơn: độ trễ (giây) từ <c>expires_at</c> tới lúc hủy, số đơn đã hết hạn và số lần quét lỗi.
/// Mục tiêu PAY-06: độ trễ không quá 60 giây. Không mang dữ liệu khách hay mã đơn.
/// </summary>
public static class OrderExpiryMetrics
{
    public const string MeterName = "Bepthaymao.OrderExpiry";
    public const string ExpiredInstrument = "bepthaymao.orders.expired";
    public const string LagInstrument = "bepthaymao.orders.expiry_lag";
    public const string FailureInstrument = "bepthaymao.orders.expiry_sweep_failures";

    private static readonly Meter _meter = new(MeterName);
    private static readonly Counter<long> _expired = _meter.CreateCounter<long>(ExpiredInstrument, "{order}", "Đơn đã hết hạn và bị hủy");
    private static readonly Histogram<double> _lag = _meter.CreateHistogram<double>(LagInstrument, "s", "Độ trễ từ hạn thanh toán tới lúc hủy");
    private static readonly Counter<long> _failures = _meter.CreateCounter<long>(FailureInstrument, "{sweep}", "Lần quét hết hạn lỗi và sẽ thử lại");

    internal static void RecordExpired(double lagSeconds)
    {
        _expired.Add(1);
        _lag.Record(lagSeconds);
    }

    internal static void RecordFailure() => _failures.Add(1);
}
