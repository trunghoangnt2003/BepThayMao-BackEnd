using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Application.Orders;
using Bepthaymao.Application.Payments;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Domain.Payments;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Payments;

internal sealed partial class PaymentProcessor(AppDbContext db, ICurrentUser user, TimeProvider clock) : IPaymentOperations
{
    [GeneratedRegex(@"(?<![A-Z0-9])BTM[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{6}(?![A-Z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex OrderCodePattern();

    public async Task ReceiveAsync(SePayTransfer transfer, string payload, CancellationToken cancellationToken)
    {
        if (transfer.Id <= 0 || transfer.TransferAmount <= 0 || transfer.Content?.Length > 10000
            || string.IsNullOrWhiteSpace(transfer.AccountNumber) || transfer.AccountNumber.Length > 100
            || !DateTime.TryParseExact(transfer.TransactionDate, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
            throw InvalidTransfer();
        if (transfer.TransferType != "in") return;
        DateTimeOffset occurredAt = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).ToUniversalTime();
        DateTimeOffset receivedAt = clock.GetUtcNow();
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(transfer))));
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);
        string key = "sepay:" + transfer.Id.ToString(CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        Payment? existing = await db.Payments.IgnoreQueryFilters([AppDbContext.BranchScope]).SingleOrDefaultAsync(value => value.SePayTransactionId == transfer.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.PayloadHash != hash)
            {
                existing.ActionRequired = true;
                existing.Reason = "transaction_payload_conflict";
                db.OperationalAlerts.Add(new()
                {
                    Id = Guid.CreateVersion7(),
                    Type = "payment.payload_conflict",
                    BranchId = existing.BranchId,
                    OrderId = existing.OrderId,
                    CreatedAt = receivedAt
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            await tx.CommitAsync(cancellationToken);
            return;
        }
        string[] codes = OrderCodePattern().Matches((transfer.Content ?? "").ToUpperInvariant()).Select(match => match.Value).Distinct().ToArray();
        Order? order = codes.Length == 1 ? await LockOrderAsync(codes[0], cancellationToken) : null;
        string bank = NormalizeBank(transfer.Gateway);
        string account = transfer.AccountNumber.Trim();
        List<Branch> destinations = await db.Branches.IgnoreQueryFilters([AppDbContext.BranchScope])
            .Where(branch => branch.BankAccountNo == account).ToListAsync(cancellationToken);
        destinations = [.. destinations.Where(branch => NormalizeBank(branch.BankCode) == bank)];
        Payment payment = new()
        {
            Id = Guid.CreateVersion7(),
            SePayTransactionId = transfer.Id,
            Amount = transfer.TransferAmount,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            RawPayload = payload,
            PayloadHash = hash,
            ActionRequired = true,
            Reason = "unmatched_code"
        };
        if (destinations.Count == 1) { payment.BranchId = destinations[0].Id; payment.OwnerId = destinations[0].OwnerId; }
        else if (destinations.Select(branch => branch.OwnerId).Distinct().Count() == 1) payment.OwnerId = destinations[0].OwnerId;
        bool matches = order is not null && order.PaymentAccountNo == account && NormalizeBank(order.PaymentBankCode) == bank;
        if (matches && order is not null)
        {
            Branch branch = await db.Branches.IgnoreQueryFilters([AppDbContext.BranchScope]).SingleAsync(value => value.Id == order.BranchId, cancellationToken);
            payment.OrderId = order.Id; payment.BranchId = branch.Id; payment.OwnerId = branch.OwnerId;
            if (payment.Amount >= order.ItemsTotal)
            {
                payment.Status = "Matched";
                payment.OverpaidAmount = payment.Amount - order.ItemsTotal;
                bool changed = order.ReceivePayment(payment.Amount, occurredAt, receivedAt);
                payment.ActionRequired = !changed || payment.OverpaidAmount > 0;
                payment.Reason = changed ? (order.LatePayment ? "late_restored" : "matched")
                    : order.Status == OrderStatus.Cancelled ? "cancelled_by_user" : "already_paid";
                if (changed) await RecordPaidAsync(order, "QR_SEPAY", cancellationToken);
                if (order.LatePayment) AddEvent("payment.late", order, new { orderId = order.Id, branchId = order.BranchId, amount = payment.Amount });
                if (payment.OverpaidAmount > 0) AddEvent("payment.overpaid", order,
                    new { orderId = order.Id, branchId = order.BranchId, overpaidAmount = payment.OverpaidAmount });
            }
            else payment.Reason = "insufficient_amount";
        }
        else if (order is not null) payment.Reason = "wrong_destination";
        db.Payments.Add(payment);
        db.PaymentAudits.Add(new() { Id = Guid.CreateVersion7(), OrderId = payment.OrderId, Action = payment.Reason, OccurredAt = receivedAt });
        if (payment.ActionRequired) db.OperationalAlerts.Add(new()
        {
            Id = Guid.CreateVersion7(),
            BranchId = payment.BranchId,
            OrderId = payment.OrderId,
            Type = "payment." + payment.Reason,
            CreatedAt = receivedAt
        });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<OrderDto> ConfirmAsync(Guid orderId, string? note, CancellationToken cancellationToken)
    {
        if (user.Role != UserRole.Owner || user.OwnerId is null || user.UserId is null) throw new ForbiddenException();
        if (note?.Length > 500) throw InvalidTransfer();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);
        Order? visible = await db.Orders.AsNoTracking().FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
        if (visible is null) throw new NotFoundException();
        Order order = (await LockOrderAsync(visible.Code, cancellationToken))!;
        Branch branch = await db.Branches.SingleAsync(value => value.Id == order.BranchId, cancellationToken);
        if (branch.OwnerId != user.OwnerId) throw new ForbiddenException();
        if (order.Status == OrderStatus.Cancelled && !order.CanRestoreFromExpiry)
            throw new ConflictException("status", "Đơn chủ động hủy không được khôi phục bằng xác nhận thanh toán.");
        DateTimeOffset now = clock.GetUtcNow();
        if (order.ReceivePayment(order.ItemsTotal, now, now))
        {
            db.Payments.Add(new()
            {
                Id = Guid.CreateVersion7(),
                BranchId = branch.Id,
                OwnerId = branch.OwnerId,
                OrderId = order.Id,
                Method = "MANUAL",
                Status = "Manual",
                Amount = order.ItemsTotal,
                OccurredAt = now,
                ReceivedAt = now,
                ConfirmedByUserId = user.UserId,
                Note = note?.Trim(),
                Reason = "owner_confirmed"
            });
            db.PaymentAudits.Add(new() { Id = Guid.CreateVersion7(), OrderId = order.Id, ActorId = user.UserId, Action = "manual_confirmed", OccurredAt = now });
            await RecordPaidAsync(order, "MANUAL", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        return order.ToDto(branch.Name, includeCustomer: true);
    }

    public async Task<IReadOnlyList<PaymentView>> ListAsync(int page, CancellationToken cancellationToken)
    {
        if (user.OwnerId is null && user.BranchId is null) throw new ForbiddenException();
        IQueryable<Order> orderCodes = db.Orders.IgnoreQueryFilters([AppDbContext.BranchScope]);
        IQueryable<Payment> query = db.Payments.AsNoTracking();
        query = user.Role == UserRole.Owner ? query.Where(payment => payment.OwnerId == user.OwnerId)
            : query.Where(payment => payment.BranchId == user.BranchId);
        return await query.OrderByDescending(payment => payment.ReceivedAt).ThenBy(payment => payment.Id)
            .Skip((Math.Clamp(page, 1, 100000) - 1) * 20).Take(20)
            .Select(payment => new PaymentView(payment.Id, payment.OrderId, payment.BranchId, payment.Method, payment.Status,
                payment.Amount, payment.OverpaidAmount, payment.Reason, payment.ActionRequired, payment.OccurredAt, payment.ReceivedAt,
                payment.ConfirmedByUserId, payment.Note, payment.SePayTransactionId,
                orderCodes.Where(order => order.Id == payment.OrderId).Select(order => order.Code).FirstOrDefault())).ToListAsync(cancellationToken);
    }

    private async Task<Order?> LockOrderAsync(string code, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync($"SELECT id FROM orders WHERE code = {code} FOR UPDATE", cancellationToken);
        return await db.Orders.IgnoreQueryFilters([AppDbContext.BranchScope]).Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.Code == code, cancellationToken);
    }

    private async Task RecordPaidAsync(Order order, string method, CancellationToken cancellationToken)
    {
        foreach (OrderItem item in order.Items.OrderBy(value => value.DishId))
            await db.Database.ExecuteSqlAsync($"INSERT INTO dish_stats (dish_id, branch_id, order_count) VALUES ({item.DishId}, {order.BranchId}, {item.Quantity}) ON CONFLICT (dish_id, branch_id) DO UPDATE SET order_count = dish_stats.order_count + EXCLUDED.order_count", cancellationToken);
        AddEvent("order.paid", order, new { orderId = order.Id, code = order.Code, branchId = order.BranchId, paidAt = order.PaidAt, method });
    }

    private void AddEvent(string type, Order order, object data) => db.OutboxMessages.Add(new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        OccurredAt = clock.GetUtcNow(),
        Payload = JsonSerializer.Serialize(data),
    });

    private static ValidationException InvalidTransfer() => new(new Dictionary<string, string[]> { ["payment"] = ["Thông tin thanh toán không hợp lệ."] });

    internal static string NormalizeBank(string? value) => (value ?? "").Trim().ToUpperInvariant() switch
    {
        "VIETINBANK" or "ICB" => "970415",
        "VIETCOMBANK" or "VCB" => "970436",
        "MB" or "MBBANK" => "970422",
        "TECHCOMBANK" or "TCB" => "970407",
        "BIDV" => "970418",
        "AGRIBANK" or "VBA" => "970405",
        "ACB" => "970416",
        "VPBANK" or "VPB" => "970432",
        "SACOMBANK" or "STB" => "970403",
        "TPBANK" or "TPB" => "970423",
        string bank => bank,
    };
}
