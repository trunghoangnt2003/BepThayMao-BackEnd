using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Domain.Branches;
using FluentValidation;
using FluentValidation.Results;

namespace Bepthaymao.Application.Branches.Manager.UpdateManagerBranchSettings;

public sealed record UpdateManagerBranchSettingsCommand(BranchSettingsData Settings) : ICommand<ManagerBranchView>;

internal sealed class UpdateManagerBranchSettingsCommandValidator : AbstractValidator<UpdateManagerBranchSettingsCommand>
{
    private static readonly BranchSettingsRules<BranchSettingsData> _rules = new();

    public UpdateManagerBranchSettingsCommandValidator()
    {
        RuleFor(command => command.Settings).NotNull().WithMessage("Thiếu thông số cơ sở.");
        // Thân yêu cầu là phẳng nên khóa lỗi cũng phẳng (openTime, zaloRecipientIds...), không có tiền tố settings.
        RuleFor(command => command.Settings).Custom((settings, context) =>
        {
            if (settings is null) return;
            foreach (ValidationFailure failure in _rules.Validate(settings).Errors) context.AddFailure(failure);
        });
    }
}

internal sealed class UpdateManagerBranchSettingsCommandHandler(ICurrentUser currentUser, IBranchRepository repository)
    : ICommandHandler<UpdateManagerBranchSettingsCommand, ManagerBranchView>
{
    public async Task<ManagerBranchView> Handle(UpdateManagerBranchSettingsCommand command, CancellationToken cancellationToken)
    {
        Guid branchId = currentUser.BranchId ?? throw new ForbiddenException();
        Branch branch = await repository.FindAsync(branchId, cancellationToken) ?? throw new NotFoundException();
        if (branch.Id != branchId) throw new ForbiddenException();
        BranchSettingsData settings = command.Settings;
        // Chỉ năm trường của BranchSettings; danh tính, vị trí, trạng thái và tài khoản nhận tiền không có đường vào đây.
        branch.ApplyManagerSettings(settings.Phone, BranchSettingsPatterns.ParseTime(settings.OpenTime),
            BranchSettingsPatterns.ParseTime(settings.CloseTime), settings.AbandonedThresholdMinutes, settings.ZaloRecipientIds);
        return ManagerBranchView.From(branch);
    }
}
