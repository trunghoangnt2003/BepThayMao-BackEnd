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
    public const int CancelReasonMaxLength = 200;

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

    /// <summary>Nguyên nhân hủy; null khi đơn chưa bị hủy hoặc đã được khôi phục. <see cref="CancelReason"/> chỉ là lý do tự do của nhân sự.</summary>
    public CancellationKind? CancellationKind { get; private set; }
    public CancelReasonCode? CancelReasonCode { get; private set; }
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
    public DateTimeOffset? PaidRecordedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? AbandonedAlertedAt { get; private set; }
    public DateTimeOffset? LastTransitionAt { get; private set; }
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

    /// <summary>Đơn tự hết hạn: nhận đủ tiền thì tự khôi phục Paid (D-02); chủ động hủy thì không (D-03).</summary>
    public bool CanRestoreFromExpiry => Status == OrderStatus.Cancelled && CancellationKind == Orders.CancellationKind.Expired;

    /// <summary>
    /// Nhận đủ tiền đúng một lần. Trả true khi đơn chuyển sang Paid (kể cả khôi phục từ hết hạn).
    /// Đơn chủ động hủy chỉ ghi nhận khoản tiền (LatePaid) và trả false; không bao giờ hồi sinh hay lùi trạng thái đã đi tiếp.
    /// </summary>
    public bool ReceivePayment(long amount, DateTimeOffset occurredAt, DateTimeOffset receivedAt)
    {
        if (amount <= 0 || amount < ItemsTotal || PaymentStatus != PaymentStatus.Unpaid) return false;
        LatePayment = occurredAt >= ExpiresAt;
        PaidAt = occurredAt.ToUniversalTime();
        PaidRecordedAt = receivedAt.ToUniversalTime();
        OverpaidAmount = amount - ItemsTotal;
        Overpaid = OverpaidAmount > 0;
        if (Status == OrderStatus.Cancelled && !CanRestoreFromExpiry)
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
        CancellationKind = null;
        CancelReasonCode = null;
        LastTransitionAt = receivedAt.ToUniversalTime();
        return true;
    }

    /// <summary>Nhận đơn đã thanh toán để chuẩn bị món.</summary>
    public void Accept(DateTimeOffset now)
    {
        if (Status != OrderStatus.Paid)
            throw new DomainRuleException("status", "Chỉ nhận được đơn đã thanh toán.");
        DateTimeOffset utc = now.ToUniversalTime();
        Status = OrderStatus.Accepted;
        AcceptedAt = utc;
        LastTransitionAt = utc;
    }

    /// <summary>Chuyển đơn đã nhận sang đang chuẩn bị.</summary>
    public void StartPreparing(DateTimeOffset now)
    {
        if (Status != OrderStatus.Accepted)
            throw new DomainRuleException("status", "Chỉ chuẩn bị được đơn đã nhận.");
        Status = OrderStatus.Preparing;
        LastTransitionAt = now.ToUniversalTime();
    }

    /// <summary>Chuyển đơn đang chuẩn bị sang đang giao.</summary>
    public void StartDelivering(DateTimeOffset now)
    {
        if (Status != OrderStatus.Preparing)
            throw new DomainRuleException("status", "Chỉ giao được đơn đang chuẩn bị.");
        Status = OrderStatus.Delivering;
        LastTransitionAt = now.ToUniversalTime();
    }

    /// <summary>Hoàn tất đơn đang giao.</summary>
    public void Complete(DateTimeOffset now)
    {
        if (Status != OrderStatus.Delivering)
            throw new DomainRuleException("status", "Chỉ hoàn tất được đơn đang giao.");
        DateTimeOffset utc = now.ToUniversalTime();
        Status = OrderStatus.Completed;
        CompletedAt = utc;
        LastTransitionAt = utc;
    }

    public bool Expire(DateTimeOffset now)
    {
        if (Status != OrderStatus.PendingPayment || PaymentStatus != PaymentStatus.Unpaid || now < ExpiresAt) return false;
        Cancel(Orders.CancellationKind.Expired, null, now);
        return true;
    }

    public bool IsPaymentOpen(DateTimeOffset now) =>
        Status == OrderStatus.PendingPayment && PaymentStatus == PaymentStatus.Unpaid && now < ExpiresAt;

    /// <summary>Khách hủy đơn chưa thanh toán; đơn đã thanh toán hoặc đã hủy thì không hủy được.</summary>
    public void CancelByCustomer(DateTimeOffset now)
    {
        if (Status != OrderStatus.PendingPayment || PaymentStatus != PaymentStatus.Unpaid)
            throw new DomainRuleException("status", "Đơn này không còn hủy được.");
        Cancel(Orders.CancellationKind.ByCustomer, null, now);
    }

    /// <summary>Cơ sở hủy đơn kèm mã lý do (đơn chưa nhận, đã thanh toán hoặc đã nhận); tiền đã thu hoàn thủ công, không tự hoàn.</summary>
    public void CancelByStaff(CancelReasonCode code, string? note, DateTimeOffset now)
    {
        if (!Enum.IsDefined(code))
            throw new DomainRuleException("reasonCode", "Mã lý do hủy không hợp lệ.");
        string trimmed = note?.Trim() ?? "";
        if (code == Orders.CancelReasonCode.Other && trimmed.Length == 0)
            throw new DomainRuleException("reason", "Phải nhập lý do hủy đơn.");
        if (trimmed.Length > CancelReasonMaxLength)
            throw new DomainRuleException("reason", $"Lý do hủy tối đa {CancelReasonMaxLength} ký tự.");
        if (Status is not (OrderStatus.PendingPayment or OrderStatus.Paid or OrderStatus.Accepted))
            throw new DomainRuleException("status", "Đơn này không còn hủy được.");
        CancelReasonCode = code;
        string resolvedReason = trimmed.Length > 0 ? trimmed : CancelReasonCodes.Label(code);
        Cancel(Orders.CancellationKind.ByStaff, resolvedReason, now);
    }

    private void Cancel(CancellationKind kind, string? reason, DateTimeOffset now)
    {
        DateTimeOffset utc = now.ToUniversalTime();
        Status = OrderStatus.Cancelled;
        CancelledAt = utc;
        CancellationKind = kind;
        CancelReason = reason;
        if (kind != Orders.CancellationKind.ByStaff) CancelReasonCode = null;
        LastTransitionAt = utc;
    }

    /// <summary>Đánh dấu đã phát cảnh báo đơn Paid chưa nhận quá ngưỡng (đúng một lần).</summary>
    public bool MarkAbandonedAlerted(DateTimeOffset now)
    {
        if (Status != OrderStatus.Paid || AbandonedAlertedAt is not null) return false;
        AbandonedAlertedAt = now.ToUniversalTime();
        return true;
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
