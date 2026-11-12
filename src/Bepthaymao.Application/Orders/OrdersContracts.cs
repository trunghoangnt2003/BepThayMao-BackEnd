using System.Security.Cryptography;
using System.Text;
using Bepthaymao.Domain.Orders;

namespace Bepthaymao.Application.Orders;

public interface IOrderStore
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);
    void Add(Order order);
    /// <summary>Đọc ngoài filter cho luồng công khai; người gọi phải kiểm số điện thoại.</summary>
    Task<Order?> FindByCodeAsync(string code, CancellationToken cancellationToken);
    /// <summary>Đơn đi qua filter BranchScope (Quản lý: cơ sở mình; Owner: mọi cơ sở của mình).</summary>
    Task<IReadOnlyList<Order>> ListAsync(Guid? branchId, OrderStatus? status, PaymentStatus? paymentStatus, int page, int pageSize,
        CancellationToken cancellationToken);
    /// <summary>Tên và tài khoản nhận tiền của cơ sở, đọc ngoài filter (cơ sở có thể đã tắt sau khi đặt).</summary>
    Task<IReadOnlyDictionary<Guid, OrderBranchInfo>> BranchesAsync(IEnumerable<Guid> branchIds, CancellationToken cancellationToken);
}

public sealed record OrderBranchInfo(Guid Id, string Name, string? BankCode, string? BankAccountNo);

/// <summary>Dựng dữ liệu QR chuyển khoản; trả URI ảnh (data URI SVG cho VietQR).</summary>
public interface IPaymentQrService
{
    string CreateQrUrl(string bankCode, string accountNo, long amount, string transferContent);
}

public sealed record OrderItemDto(string DishName, long UnitPrice, int Quantity, long LineTotal);

public sealed record OrderDto(Guid Id, string Code, Guid BranchId, string BranchName, OrderStatus Status, PaymentStatus PaymentStatus,
    bool IsNextDay, DateOnly DeliveryDate, double? DistanceKm, long ItemsTotal, DateTimeOffset ExpiresAt, bool LatePayment,
    IReadOnlyList<OrderItemDto> Items, DateTimeOffset CreatedAt, string? CustomerName, string? CustomerPhone, string? DeliveryAddress, string? Note);

public sealed record PaymentDto(string QrUrl, long Amount, string TransferContent, DateTimeOffset ExpiresAt);

public sealed record OrderCreatedDto(OrderDto Order, PaymentDto Payment, string ShippingNote);

public sealed record OrderTrackingDto(OrderDto Order, PaymentDto? Payment);

internal static class OrderMapping
{
    public const string ShippingNote = "Phí ship trả riêng khi nhận hàng.";

    /// <summary>Thông tin khách chỉ trả cho Quản lý/Owner; khách tra cứu đã biết số điện thoại của mình.</summary>
    public static OrderDto ToDto(this Order order, string branchName, bool includeCustomer) => new(order.Id, order.Code, order.BranchId, branchName,
        order.Status, order.PaymentStatus, order.IsNextDay, order.DeliveryDate, order.DistanceKm, order.ItemsTotal, order.ExpiresAt,
        order.PaymentStatus == PaymentStatus.LatePaid,
        [.. order.Items.Select(item => new OrderItemDto(item.DishName, item.UnitPrice, item.Quantity, item.LineTotal))], order.CreatedAt,
        includeCustomer ? order.CustomerName : null, includeCustomer ? order.CustomerPhone : null,
        includeCustomer ? order.DeliveryAddress : null, includeCustomer ? order.Note : null);
}

public static class PhoneNumbers
{
    /// <summary>Bỏ khoảng trắng, dấu chấm, gạch; null nếu không khớp mẫu hợp đồng <c>^(\+84|0)[0-9]{9}$</c>.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null) return null;
        string compact = new([.. value.Where(character => character is not (' ' or '.' or '-'))]);
        bool valid = compact.StartsWith("+84", StringComparison.Ordinal)
            ? compact.Length == 12 && compact[3..].All(char.IsAsciiDigit)
            : compact.Length == 10 && compact[0] == '0' && compact.All(char.IsAsciiDigit);
        return valid ? compact : null;
    }

    /// <summary>So sánh thời gian hằng, coi +84xxx và 0xxx là một.</summary>
    public static bool SameNumber(string? left, string? right)
    {
        static string Local(string value) => value.StartsWith("+84", StringComparison.Ordinal) ? "0" + value[3..] : value;
        string? a = Normalize(left);
        string? b = Normalize(right);
        if (a is null || b is null) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Local(a)), Encoding.ASCII.GetBytes(Local(b)));
    }
}

public static class OpeningHours
{
    private static readonly TimeZoneInfo _vietnam = ResolveVietnam();

    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, _vietnam);

    /// <summary>Hai mốc tính trong giờ, không qua nửa đêm (W7, SC-003).</summary>
    public static bool IsOpen(TimeOnly open, TimeOnly close, DateTimeOffset utcNow)
    {
        TimeOnly now = TimeOnly.FromDateTime(ToLocal(utcNow).DateTime);
        TimeOnly minute = new(now.Hour, now.Minute);
        return minute >= open && minute <= close;
    }

    /// <summary>Ngày giao: trước giờ mở là hôm nay, sau giờ đóng là hôm sau.</summary>
    public static DateOnly DeliveryDate(TimeOnly open, TimeOnly close, DateTimeOffset utcNow)
    {
        DateTime local = ToLocal(utcNow).DateTime;
        DateOnly today = DateOnly.FromDateTime(local);
        return TimeOnly.FromDateTime(local) > close && !IsOpen(open, close, utcNow) ? today.AddDays(1) : today;
    }

    private static TimeZoneInfo ResolveVietnam()
    {
        foreach (string id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? zone)) return zone;
        return TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    }
}

public static class Distance
{
    private const double _earthRadiusKm = 6371.0088;

    public static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;
        double dLat = Radians(lat2 - lat1);
        double dLng = Radians(lng2 - lng1);
        double a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return Math.Round(_earthRadiusKm * 2 * Math.Asin(Math.Min(1, Math.Sqrt(a))), 1);
    }
}

public static class OrderCodes
{
    /// <summary>Bảng ký tự không nhầm lẫn (bỏ 0, 1, I, L, O).</summary>
    private const string _alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public static string Next() => "BTM" + new string([.. Enumerable.Range(0, 6).Select(_ => _alphabet[RandomNumberGenerator.GetInt32(_alphabet.Length)])]);

    public static string Normalize(string? code) => (code ?? "").Trim().ToUpperInvariant();
}
