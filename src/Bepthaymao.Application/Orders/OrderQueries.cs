using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Branches;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Common;
using Bepthaymao.Domain.Orders;
using FluentValidation;

namespace Bepthaymao.Application.Orders;

public sealed record GetOrderQuery(string Code, string? Phone, IReadOnlyList<string> AccessTokens) : IQuery<OrderTrackingDto>;

public sealed record CancelOrderCommand(string Code, string? Phone, IReadOnlyList<string> AccessTokens) : ICommand<OrderDto>;

/// <summary>Khách đổi mã đơn + số điện thoại lấy cookie btm_don (xem/hủy đơn mà không cần phone trên URL).</summary>
public sealed record GrantOrderAccessCommand(string Code, string Phone) : ICommand<IssuedOrderAccess>;

internal sealed class GrantOrderAccessCommandValidator : AbstractValidator<GrantOrderAccessCommand>
{
    public GrantOrderAccessCommandValidator()
    {
        RuleFor(command => command.Phone).NotEmpty().WithMessage("Nhập số điện thoại.").MaximumLength(20).WithMessage("Số điện thoại tối đa 20 ký tự.");
    }
}

/// <summary>
/// Quản lý: đơn cơ sở mình (không nhận branchId). Owner: mọi cơ sở của mình hoặc một cơ sở; branchId của Owner khác trả 403 (FR-003a).
/// </summary>
public sealed record ListOrdersQuery(Guid? BranchId, OrderStatus? Status, PaymentStatus? PaymentStatus, int Page) : IQuery<IReadOnlyList<OrderDto>>;

internal static class PublicOrderAccess
{
    /// <summary>Sai mã, sai số điện thoại, token sai/hết hạn/của đơn khác và đơn không tồn tại đều trả cùng một 404 (không lộ tồn tại, K5).</summary>
    public static async Task<Order> FindAsync(IOrderStore store, IOrderAccessGrants grants, string code, string? phone,
        IReadOnlyList<string> accessTokens, CancellationToken cancellationToken)
    {
        Order? order = await store.FindByCodeAsync(OrderCodes.Normalize(code), cancellationToken);
        bool phoneMatches = !string.IsNullOrWhiteSpace(phone) && PhoneNumbers.SameNumber(order?.CustomerPhone ?? "0000000000", phone);
        bool tokenMatches = await grants.AllowsAsync(order?.Id ?? Guid.Empty, accessTokens, cancellationToken);
        return order is not null && (phoneMatches || tokenMatches) ? order : throw new NotFoundException();
    }
}

internal sealed class GetOrderQueryHandler(IOrderStore store, IOrderAccessGrants grants, IPaymentQrService qr, TimeProvider clock)
    : IQueryHandler<GetOrderQuery, OrderTrackingDto>
{
    public async Task<OrderTrackingDto> Handle(GetOrderQuery query, CancellationToken cancellationToken)
    {
        Order order = await PublicOrderAccess.FindAsync(store, grants, query.Code, query.Phone, query.AccessTokens, cancellationToken);
        OrderBranchInfo branch = (await store.BranchesAsync([order.BranchId], cancellationToken))[order.BranchId];
        PaymentDto? payment = order.IsPaymentOpen(clock.GetUtcNow()) && branch.BankCode is not null && branch.BankAccountNo is not null
            ? new(qr.CreateQrUrl(branch.BankCode, branch.BankAccountNo, order.ItemsTotal, order.Code), order.ItemsTotal, order.Code, order.ExpiresAt)
            : null;
        return new(order.ToDto(branch.Name, includeCustomer: false), payment);
    }
}

internal sealed class GrantOrderAccessCommandHandler(IOrderStore store, IOrderAccessGrants grants, IUnitOfWork unitOfWork)
    : ICommandHandler<GrantOrderAccessCommand, IssuedOrderAccess>
{
    public async Task<IssuedOrderAccess> Handle(GrantOrderAccessCommand command, CancellationToken cancellationToken)
    {
        // Chỉ nhận phone, không nhận token: không cho dùng cookie đang có để đổi lấy cookie khác.
        Order order = await PublicOrderAccess.FindAsync(store, grants, command.Code, command.Phone, [], cancellationToken);
        IssuedOrderAccess access = grants.Issue(order.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return access;
    }
}

internal sealed class CancelOrderCommandHandler(IOrderStore store, IOrderAccessGrants grants, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CancelOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Order order = await PublicOrderAccess.FindAsync(store, grants, command.Code, command.Phone, command.AccessTokens, cancellationToken);
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
