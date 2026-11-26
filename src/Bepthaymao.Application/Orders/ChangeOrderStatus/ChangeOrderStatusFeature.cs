using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Branches;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Common;
using Bepthaymao.Domain.Orders;
using FluentValidation;

namespace Bepthaymao.Application.Orders.ChangeOrderStatus;

public sealed record ChangeOrderStatusCommand(Guid OrderId, OrderStatus To, CancelReasonCode? ReasonCode, string? Reason) : ICommand<OrderDto>;

internal sealed class ChangeOrderStatusCommandValidator : AbstractValidator<ChangeOrderStatusCommand>
{
    public ChangeOrderStatusCommandValidator()
    {
        RuleFor(command => command.ReasonCode)
            .NotNull()
            .WithMessage("Chọn lý do hủy đơn.")
            .When(command => command.To == OrderStatus.Cancelled);
        RuleFor(command => command.ReasonCode)
            .IsInEnum()
            .WithMessage("Mã lý do hủy không hợp lệ.")
            .When(command => command.ReasonCode is not null);
        RuleFor(command => command.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("Phải nhập lý do hủy đơn.")
            .When(command => command.To == OrderStatus.Cancelled && command.ReasonCode == CancelReasonCode.Other);
        RuleFor(command => command.Reason)
            .MaximumLength(Order.CancelReasonMaxLength)
            .WithMessage($"Lý do hủy tối đa {Order.CancelReasonMaxLength} ký tự.");
    }
}

internal sealed class ChangeOrderStatusCommandHandler(
    ICurrentUser currentUser,
    IBranchRepository branches,
    IOrderStore store,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ChangeOrderStatusCommand, OrderDto>
{
    public async Task<OrderDto> Handle(ChangeOrderStatusCommand command, CancellationToken cancellationToken)
    {
        Order? updated = null;
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await store.LockOrderByIdAsync(command.OrderId, ct);
            Order order = await store.FindByIdAsync(command.OrderId, ct) ?? throw new NotFoundException();

            if (currentUser.BranchId is Guid managerBranch)
            {
                if (order.BranchId != managerBranch) throw new NotFoundException();
            }
            else if (currentUser.OwnerId is Guid ownerId)
            {
                Branch? branch = await branches.FindAsync(order.BranchId, ct);
                if (branch is null || branch.OwnerId != ownerId) throw new NotFoundException();
            }
            else
            {
                throw new ForbiddenException();
            }

            DateTimeOffset now = clock.GetUtcNow();
            try
            {
                switch (command.To)
                {
                    case OrderStatus.Accepted:
                        order.Accept(now);
                        break;
                    case OrderStatus.Preparing:
                        order.StartPreparing(now);
                        break;
                    case OrderStatus.Delivering:
                        order.StartDelivering(now);
                        break;
                    case OrderStatus.Completed:
                        order.Complete(now);
                        break;
                    case OrderStatus.Cancelled:
                        order.CancelByStaff(command.ReasonCode!.Value, command.Reason, now);
                        break;
                    default:
                        throw new ConflictException("status", "Không chuyển được trạng thái này. Tải lại đơn để xem trạng thái mới.");
                }
            }
            catch (DomainRuleException)
            {
                throw new ConflictException("status", "Không chuyển được trạng thái này. Tải lại đơn để xem trạng thái mới.");
            }

            await unitOfWork.SaveChangesAsync(ct);
            updated = order;
        }, cancellationToken);

        Order result = updated!;
        IReadOnlyDictionary<Guid, OrderBranchInfo> branchInfos = await store.BranchesAsync([result.BranchId], cancellationToken);
        string branchName = branchInfos.TryGetValue(result.BranchId, out OrderBranchInfo? info) ? info.Name : "";
        return result.ToDto(branchName, includeCustomer: true);
    }
}
