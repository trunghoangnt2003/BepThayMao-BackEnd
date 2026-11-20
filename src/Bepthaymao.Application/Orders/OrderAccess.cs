namespace Bepthaymao.Application.Orders;

/// <summary>Token ngẫu nhiên vừa cấp cho một đơn; giá trị thô chỉ sống trong cookie, DB chỉ lưu băm.</summary>
public sealed record IssuedOrderAccess(string Token, DateTimeOffset ExpiresAt);

/// <summary>Cấp và kiểm token gắn đơn (cookie btm_don); không lưu token thô (SEC-01, T-02-01/02/06).</summary>
public interface IOrderAccessGrants
{
    /// <summary>Thêm một bản ghi cấp token vào unit of work đang mở; người gọi tự SaveChanges.</summary>
    IssuedOrderAccess Issue(Guid orderId);

    /// <summary>Đúng khi một trong các token còn hạn và thuộc đúng <paramref name="orderId"/>.</summary>
    Task<bool> AllowsAsync(Guid orderId, IReadOnlyList<string> rawTokens, CancellationToken cancellationToken);
}
