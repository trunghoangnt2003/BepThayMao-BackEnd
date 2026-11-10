using Bepthaymao.Domain.Branches;
using FluentValidation;

namespace Bepthaymao.Application.Branches.UpsertBranches;

internal sealed class UpsertBranchesCommandValidator : AbstractValidator<UpsertBranchesCommand>
{
    public UpsertBranchesCommandValidator()
    {
        RuleFor(command => command.Branches).NotEmpty()
            .Must(items => items is null || items.Select(item => item.Id).Distinct().Count() == items.Count)
            .WithMessage("Danh sách cơ sở có mã trùng nhau.");
        RuleForEach(command => command.Branches).ChildRules(item =>
        {
            item.RuleFor(branch => branch.Id).NotEmpty();
            item.RuleFor(branch => branch.Name).NotEmpty().MaximumLength(Branch.NameMaxLength);
            item.RuleFor(branch => branch.Address).NotEmpty().MaximumLength(Branch.AddressMaxLength);
            item.RuleFor(branch => branch.Phone).NotEmpty().MaximumLength(Branch.PhoneMaxLength);
            item.RuleFor(branch => branch.CloseTime).GreaterThan(branch => branch.OpenTime)
                .WithMessage("Giờ đóng phải sau giờ mở trong cùng một ngày.");
        });
    }
}
