using Bepthaymao.Application.Orders;

namespace Bepthaymao.Application.Payments;

public sealed record SePayTransfer(long Id, string? Gateway, string? TransactionDate, string? AccountNumber,
    string? Content, string? TransferType, long TransferAmount, string? Code = null);
public sealed record PaymentView(Guid Id, Guid? OrderId, Guid? BranchId, string Method, string Status, long Amount,
    long OverpaidAmount, string Reason, bool ActionRequired, DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt,
    Guid? ConfirmedByUserId, string? Note, long? TransactionId = null, string? OrderCode = null);
public interface IPaymentOperations
{
    Task ReceiveAsync(SePayTransfer transfer, string payload, CancellationToken cancellationToken);
    Task<OrderDto> ConfirmAsync(Guid orderId, string? note, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentView>> ListAsync(int page, CancellationToken cancellationToken);
}
