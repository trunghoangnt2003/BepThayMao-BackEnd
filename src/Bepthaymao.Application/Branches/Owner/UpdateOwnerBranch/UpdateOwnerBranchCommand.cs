using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Branches.Owner.CreateOwnerBranch;
using Bepthaymao.Domain.Branches;
using FluentValidation;
using FluentValidation.Results;

namespace Bepthaymao.Application.Branches.Owner.UpdateOwnerBranch;

public sealed record UpdateOwnerBranchCommand(Guid BranchId, BranchOwnerSettingsData Branch) : ICommand<BranchResponse>;

internal sealed class UpdateOwnerBranchCommandValidator : AbstractValidator<UpdateOwnerBranchCommand>
{
    private static readonly BranchOwnerSettingsValidator _rules = new();

    public UpdateOwnerBranchCommandValidator()
    {
        RuleFor(command => command.Branch).NotNull().WithMessage("Thiếu thông tin cơ sở.");
        // Thân yêu cầu là phẳng nên khóa lỗi cũng phẳng (openTime, lat...), không có tiền tố branch như lúc tạo.
        RuleFor(command => command.Branch).Custom((branch, context) =>
        {
            if (branch is null) return;
            foreach (ValidationFailure failure in _rules.Validate(branch).Errors) context.AddFailure(failure);
        });
    }
}

internal sealed class UpdateOwnerBranchCommandHandler(ICurrentUser currentUser, IBranchRepository repository,
    IOwnerBranchReader reader, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateOwnerBranchCommand, BranchResponse>
{
    public async Task<BranchResponse> Handle(UpdateOwnerBranchCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        // Filter BranchScope lọc cơ sở của Owner khác nên kết quả null: khớp 404, không lộ sự tồn tại.
        Branch branch = await repository.FindAsync(command.BranchId, cancellationToken) ?? throw new NotFoundException();
        if (branch.OwnerId != ownerId) throw new ForbiddenException();
        BranchOwnerSettingsData data = command.Branch;
        branch.ApplyOwnerDetails(new(data.Name, data.Address, data.Phone, data.Lat, data.Lng,
            BranchSettingsPatterns.ParseTime(data.OpenTime), BranchSettingsPatterns.ParseTime(data.CloseTime),
            data.AbandonedThresholdMinutes, data.ZaloRecipientIds, data.BankAccountNo, data.BankCode, data.SepayAccountRef, data.IsActive));
        // Lưu trước rồi đọc lại để phản hồi mang tên đăng nhập quản lý và đúng dữ liệu đã ghi.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        OwnerBranchView saved = await reader.GetAsync(command.BranchId, cancellationToken) ?? throw new NotFoundException();
        return saved.Branch;
    }
}
