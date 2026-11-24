namespace Bepthaymao.Domain.Payments;

/// <summary>Sổ nhận tiền bền vững; giao dịch không xác định đơn vẫn được giữ để Owner đối chiếu.</summary>
public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? OwnerId { get; set; }
    public long? SePayTransactionId { get; set; }
    public string Method { get; set; } = "QR_SEPAY";
    public string Status { get; set; } = "Unmatched";
    public long Amount { get; set; }
    public long OverpaidAmount { get; set; }
    public string Reason { get; set; } = "";
    public bool ActionRequired { get; set; }
    public string PayloadHash { get; set; } = "";
    public string RawPayload { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public string? Note { get; set; }
}

public sealed class PaymentAudit
{
    public Guid Id { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
