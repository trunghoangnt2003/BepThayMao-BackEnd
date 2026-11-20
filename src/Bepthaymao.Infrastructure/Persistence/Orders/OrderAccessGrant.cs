namespace Bepthaymao.Infrastructure.Persistence.Orders;

/// <summary>Bản ghi cấp token gắn đơn; chỉ lưu băm SHA-256 của token (T-02-06).</summary>
public sealed class OrderAccessGrant
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
