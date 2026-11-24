using System.ComponentModel.DataAnnotations.Schema;
using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Orders;

/// <summary>Nguyên nhân hủy đơn (D-04): hết hạn thì tiền về đủ được khôi phục, chủ động hủy thì giữ hủy.</summary>
public enum CancellationKind
{
    Expired,
    ByCustomer,
    ByStaff,
}

public enum OrderStatus
{
    PendingPayment,
    Paid,
    Accepted,
    Preparing,
    Delivering,
    Completed,
    Cancelled,
}

public enum PaymentStatus
{
    Unpaid,
    Paid,
    LatePaid,
}

/// <summary>Đơn của khách; thuộc đúng một cơ sở. Tổng tiền chỉ gồm tiền món, phí ship trả riêng (FR-011).</summary>
public sealed class Order : IBranchScoped
{
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(15);

    private readonly List<OrderItem> _items = [];

    private Order() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public Guid BranchId { get; private set; }
    public string CustomerName { get; private set; } = "";
    public string CustomerPhone { get; private set; } = "";
    public string DeliveryAddress { get; private set; } = "";
    public double? DeliveryLat { get; private set; }
    public double? DeliveryLng { get; private set; }
    public double? DistanceKm { get; private set; }
    public string? Note { get; private set; }
    public long ItemsTotal { get; private set; }
    public OrderStatus Status { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public bool IsNextDay { get; private set; }
    public DateOnly DeliveryDate { get; private set; }
    public DateTimeOffset? NextDayAcknowledgedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }

    // Khung biên dịch cho bước RED (03-01): chưa lưu cột và chưa có hành vi.
    [NotMapped]
    public CancellationKind? CancellationKind { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? IdempotencyKey { get; private set; }
    public string DedupeHash { get; private set; } = "";
    public IReadOnlyList<OrderItem> Items => _items;

    public static Order Place(Guid id, string code, Guid branchId, OrderCustomer customer, IReadOnlyList<OrderItem> items,
        bool isNextDay, DateOnly deliveryDate, DateTimeOffset now, Guid? idempotencyKey = null, string dedupeHash = "")
    {
        if (items.Count == 0) throw new DomainRuleException("items", "Đơn phải có ít nhất một món.");
        Order order = new()
        {
            Id = id,
            Code = code,
            BranchId = branchId,
            CustomerName = customer.Name,
            CustomerPhone = customer.Phone,
            DeliveryAddress = customer.Address,
            DeliveryLat = customer.Lat,
            DeliveryLng = customer.Lng,
            DistanceKm = customer.DistanceKm,
            Note = string.IsNullOrWhiteSpace(customer.Note) ? null : customer.Note.Trim(),
            ItemsTotal = items.Sum(item => item.LineTotal),
            Status = OrderStatus.PendingPayment,
            PaymentStatus = PaymentStatus.Unpaid,
            IsNextDay = isNextDay,
            DeliveryDate = deliveryDate,
            NextDayAcknowledgedAt = isNextDay ? now.ToUniversalTime() : null,
            ExpiresAt = now.ToUniversalTime() + PaymentWindow,
            CreatedAt = now.ToUniversalTime(),
            IdempotencyKey = idempotencyKey,
            DedupeHash = dedupeHash,
        };
        order._items.AddRange(items);
        return order;
    }

    public string? PaymentBankCode { get; private set; }
    public string? PaymentAccountNo { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public bool LatePayment { get; private set; }
    public bool Overpaid { get; private set; }
    public long OverpaidAmount { get; private set; }
    public bool RestoredFromExpiry { get; private set; }

    public void CapturePaymentDestination(string bankCode, string accountNo)
    {
        if (PaymentBankCode is not null) return;
        PaymentBankCode = bankCode;
        PaymentAccountNo = accountNo;
    }

    /// <summary>Nhận đủ tiền đúng một lần. Chỉ khôi phục đơn tự hết hạn; không hồi sinh hủy chủ động.</summary>
    public bool ReceivePayment(long amount, DateTimeOffset occurredAt, DateTimeOffset receivedAt)
    {
        if (amount < ItemsTotal || PaymentStatus != PaymentStatus.Unpaid) return false;
        LatePayment = occurredAt >= ExpiresAt;
        PaidAt = occurredAt.ToUniversalTime();
        OverpaidAmount = amount - ItemsTotal;
        Overpaid = OverpaidAmount > 0;
        if (Status == OrderStatus.Cancelled && CancelReason != "expired")
        {
            LatePayment = true;
            PaymentStatus = PaymentStatus.LatePaid;
            return false;
        }
        if (Status != OrderStatus.PendingPayment && Status != OrderStatus.Cancelled) return false;
        RestoredFromExpiry = Status == OrderStatus.Cancelled;
        Status = OrderStatus.Paid;
        PaymentStatus = LatePayment ? PaymentStatus.LatePaid : PaymentStatus.Paid;
        CancelledAt = null;
        CancelReason = null;
        return true;
    }

    public bool Expire(DateTimeOffset now)
    {
        if (Status != OrderStatus.PendingPayment || PaymentStatus != PaymentStatus.Unpaid || now < ExpiresAt) return false;
        Status = OrderStatus.Cancelled;
        CancelledAt = now.ToUniversalTime();
        CancelReason = "expired";
        return true;
    }

    public bool IsPaymentOpen(DateTimeOffset now) =>
        Status == OrderStatus.PendingPayment && PaymentStatus == PaymentStatus.Unpaid && now < ExpiresAt;

    /// <summary>Khách hủy đơn chưa thanh toán; đơn đã thanh toán hoặc đã hủy thì không hủy được.</summary>
    public void CancelByCustomer(DateTimeOffset now)
    {
        if (Status != OrderStatus.PendingPayment || PaymentStatus != PaymentStatus.Unpaid)
            throw new DomainRuleException("status", "Đơn này không còn hủy được.");
        Status = OrderStatus.Cancelled;
        CancelledAt = now.ToUniversalTime();
        CancelReason = "customer";
    }

    /// <summary>Khung biên dịch cho bước RED (03-01): chưa có hành vi.</summary>
    public void CancelByStaff(string reason, DateTimeOffset now)
    {
        _ = reason;
        _ = now;
        if (Status == OrderStatus.Cancelled) return;
    }
}

public sealed record OrderCustomer(string Name, string Phone, string Address, double? Lat, double? Lng, double? DistanceKm, string? Note);

/// <summary>Dòng món chụp lại tên và giá lúc đặt; sửa món sau đó không đổi đơn cũ.</summary>
public sealed class OrderItem
{
    private OrderItem() { }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid DishId { get; private set; }
    public string DishName { get; private set; } = "";
    public long UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public long LineTotal { get; private set; }

    public static OrderItem Create(Guid id, Guid dishId, string dishName, long unitPrice, int quantity)
    {
        if (quantity is < 1 or > 99) throw new DomainRuleException("quantity", "Số lượng từ 1 đến 99.");
        return new() { Id = id, DishId = dishId, DishName = dishName, UnitPrice = unitPrice, Quantity = quantity, LineTotal = unitPrice * quantity };
    }
}
