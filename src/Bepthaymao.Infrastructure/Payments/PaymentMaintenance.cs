using System.Text.Json;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Orders;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bepthaymao.Infrastructure.Payments;

/// <summary>
/// Batch nền hữu hạn cho thanh toán, mỗi lần gọi thuộc một DI scope hệ thống tường minh (T-03-05).
/// Hết hạn đi qua miền (<see cref="Order.Expire"/>) và chung thứ tự khóa với settlement: khóa dòng đơn, không khóa nào khác.
/// </summary>
public sealed class PaymentMaintenance(AppDbContext db, TimeProvider clock)
{
    /// <summary>Số đơn tối đa một lần gọi: giữ transaction ngắn, phần còn lại để lần quét kế tiếp.</summary>
    public const int BatchSize = 100;

    /// <summary>Token làm mới hết hạn được giữ thêm chừng này trước khi xóa, tránh đua với lệch đồng hồ giữa các bản sao.</summary>
    private static readonly TimeSpan _refreshTokenGrace = TimeSpan.FromDays(1);

    /// <summary>
    /// Hủy tối đa <see cref="BatchSize"/> đơn quá hạn trong một transaction ngắn. <c>FOR UPDATE SKIP LOCKED</c> bỏ qua đơn
    /// đang bị webhook/khách/Owner giữ khóa (lần quét sau lấy lại), nên hai worker không bao giờ xử lý cùng một đơn.
    /// Cập nhật đơn, audit và outbox cùng một transaction: lỗi trước commit hoàn tác tất cả.
    /// </summary>
    public async Task<int> ExpireAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);
        List<Order> orders = await db.Orders.FromSql($"SELECT * FROM orders WHERE status = 'PendingPayment' AND payment_status = 'Unpaid' AND expires_at <= {now} ORDER BY expires_at, id LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .IgnoreQueryFilters([AppDbContext.BranchScope]).ToListAsync(cancellationToken);
        List<double> lags = [];
        foreach (Order order in orders)
        {
            if (!order.Expire(now)) continue;
            lags.Add(Math.Max(0, (now - order.ExpiresAt).TotalSeconds));
            db.PaymentAudits.Add(new() { Id = Guid.CreateVersion7(), OrderId = order.Id, Action = "order_expired", OccurredAt = now });
            db.OutboxMessages.Add(new()
            {
                Id = Guid.CreateVersion7(),
                Type = "order.expired",
                OccurredAt = now,
                Payload = JsonSerializer.Serialize(new { orderId = order.Id, code = order.Code, branchId = order.BranchId })
            });
        }
        if (lags.Count == 0) return 0;
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        // Chỉ ghi số đo sau khi commit thành công: transaction bị hoàn tác không được tính là đã hết hạn.
        foreach (double lag in lags) OrderExpiryMetrics.RecordExpired(lag);
        return lags.Count;
    }

    /// <summary>
    /// Dọn bản ghi truy cập đơn và refresh token đã hết hạn theo batch nhỏ; không bao giờ xóa bản ghi còn hiệu lực.
    /// Cả hai loại đều bị từ chối khi đã hết hạn nên xóa không đổi hành vi xác thực.
    /// </summary>
    public async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset refreshCutoff = now - _refreshTokenGrace;
        int grants = await db.OrderAccessGrants.Where(grant => grant.ExpiresAt < now)
            .OrderBy(grant => grant.ExpiresAt).Take(BatchSize).ExecuteDeleteAsync(cancellationToken);
        int tokens = await db.RefreshTokens.Where(token => token.ExpiresAt < refreshCutoff)
            .OrderBy(token => token.ExpiresAt).Take(BatchSize).ExecuteDeleteAsync(cancellationToken);
        return grants + tokens;
    }
}
