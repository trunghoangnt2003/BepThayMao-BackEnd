using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Domain.Branches;
using FluentValidation;

namespace Bepthaymao.Application.Branches.Owner.ResetManagerPassword;

public sealed record ResetManagerPasswordCommand(Guid BranchId, string NewPassword) : ICommand<bool>;

internal sealed class ResetManagerPasswordCommandValidator : AbstractValidator<ResetManagerPasswordCommand>
{
    public ResetManagerPasswordCommandValidator() =>
        RuleFor(command => command.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128)
            .WithMessage("Mật khẩu phải từ 10 đến 128 ký tự.");
}

internal sealed class ResetManagerPasswordCommandHandler(ICurrentUser currentUser, IBranchRepository repository,
    IManagerAccountService managers, IRefreshTokenService refreshTokens, IUnitOfWork unitOfWork)
    : ICommandHandler<ResetManagerPasswordCommand, bool>
{
    public async Task<bool> Handle(ResetManagerPasswordCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        // Filter BranchScope lọc cơ sở của Owner khác nên kết quả null: khớp 404, không lộ sự tồn tại.
        Branch branch = await repository.FindAsync(command.BranchId, cancellationToken) ?? throw new NotFoundException();
        if (branch.OwnerId != ownerId) throw new ForbiddenException();
        // Đặt mật khẩu và thu hồi mọi phiên cùng một transaction: lỗi giữa chừng không để quản lý mất mật khẩu hay còn phiên cũ (D-05).
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            Guid managerId = await managers.ResetPasswordAsync(branch.Id, command.NewPassword, token);
            await refreshTokens.RevokeAllForUserAsync(managerId, token);
        }, cancellationToken);
        return true;
    }
}
