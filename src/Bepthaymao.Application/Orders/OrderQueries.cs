using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Branches;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Common;
using Bepthaymao.Domain.Orders;

namespace Bepthaymao.Application.Orders;

public sealed record GetOrderQuery(string Code, string Phone) : IQuery<OrderTrackingDto>;

public sealed record CancelOrderCommand(string Code, string Phone) : ICommand<OrderDto>;

/// <summary>
/// Quản lý: đơn cơ sở mình (không nhận branchId). Owner: mọi cơ sở của mình hoặc một cơ sở; branchId của Owner khác trả 403 (FR-003a).
/// </summary>
public sealed record ListOrdersQuery(Guid? BranchId, OrderStatus? Status, PaymentStatus? PaymentStatus, int Page) : IQuery<IReadOnlyList<OrderDto>>;

internal static class PublicOrderAccess
{
    /// <summary>Sai mã, sai số điện thoại và đơn không tồn tại đều trả cùng một 404 (không lộ tồn tại, K5).</summary>
    public static async Task<Order> FindAsync(IOrderStore store, string code, string phone, CancellationToken cancellationToken)
    {
        Order? order = await store.FindByCodeAsync(OrderCodes.Normalize(code), cancellationToken);
        bool phoneMatches = PhoneNumbers.SameNumber(order?.CustomerPhone ?? "0000000000", phone);
        return order is not null && phoneMatches ? order : throw new NotFoundException();
    }
}

internal sealed class GetOrderQueryHandler(IOrderStore store, IPaymentQrService qr, TimeProvider clock) : IQueryHandler<GetOrderQuery, OrderTrackingDto>
{
    public async Task<OrderTrackingDto> Handle(GetOrderQuery query, CancellationToken cancellationToken)
    {
        Order order = await PublicOrderAccess.FindAsync(store, query.Code, query.Phone, cancellationToken);
        OrderBranchInfo branch = (await store.BranchesAsync([order.BranchId], cancellationToken))[order.BranchId];
        PaymentDto? payment = order.IsPaymentOpen(clock.GetUtcNow()) && branch.BankCode is not null && branch.BankAccountNo is not null
            ? new(qr.CreateQrUrl(branch.BankCode, branch.BankAccountNo, order.ItemsTotal, order.Code), order.ItemsTotal, order.Code, order.ExpiresAt)
            : null;
        return new(order.ToDto(branch.Name, includeCustomer: false), payment);
    }
}

internal sealed class CancelOrderCommandHandler(IOrderStore store, IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<CancelOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Order order = await PublicOrderAccess.FindAsync(store, command.Code, command.Phone, cancellationToken);
        try
        {
            order.CancelByCustomer(clock.GetUtcNow());
        }
        catch (DomainRuleException)
        {
            throw new ConflictException("status", "Đơn này đã được thanh toán nên không hủy được. Liên hệ cơ sở nếu cần đổi.");
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        OrderBranchInfo branch = (await store.BranchesAsync([order.BranchId], cancellationToken))[order.BranchId];
        return order.ToDto(branch.Name, includeCustomer: false);
    }
}

internal sealed class ListOrdersQueryHandler(ICurrentUser currentUser, IBranchRepository branches, IOrderStore store)
    : IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>>
{
    private const int _pageSize = 20;

    public async Task<IReadOnlyList<OrderDto>> Handle(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        Guid? branchId;
        if (currentUser.BranchId is Guid managerBranch) branchId = managerBranch;
        else
        {
            Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
            branchId = query.BranchId;
            if (branchId is Guid requested)
            {
                Branch? branch = await branches.FindAsync(requested, cancellationToken);
                if (branch is null || branch.OwnerId != ownerId) throw new ForbiddenException();
            }
        }
        IReadOnlyList<Order> orders = await store.ListAsync(branchId, query.Status, query.PaymentStatus, Math.Max(1, query.Page), _pageSize, cancellationToken);
        IReadOnlyDictionary<Guid, OrderBranchInfo> names = await store.BranchesAsync(orders.Select(order => order.BranchId).Distinct(), cancellationToken);
        return [.. orders.Select(order => order.ToDto(names.TryGetValue(order.BranchId, out OrderBranchInfo? info) ? info.Name : "", includeCustomer: true))];
    }
}
