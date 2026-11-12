using Bepthaymao.Application.Branches.Owner.CreateOwnerBranch;
using FluentValidation;

namespace Bepthaymao.Application.Branches.Owner;

/// <summary>Luật cho toàn bộ <c>BranchOwnerSettings</c>; dùng chung cho tạo cơ sở và Owner sửa cơ sở.</summary>
internal sealed class BranchOwnerSettingsValidator : AbstractValidator<BranchOwnerSettingsData>
{
    public BranchOwnerSettingsValidator()
    {
        Include(new BranchSettingsRules<BranchOwnerSettingsData>());
        RuleFor(branch => branch.Name).NotEmpty().MaximumLength(200).WithMessage("Nhập tên cơ sở (tối đa 200 ký tự).");
        RuleFor(branch => branch.Address).NotEmpty().MaximumLength(500).WithMessage("Nhập địa chỉ (tối đa 500 ký tự).");
        RuleFor(branch => branch.Lat).Must(value => double.IsFinite(value) && value is >= -90 and <= 90)
            .WithMessage("Vĩ độ nằm trong khoảng từ -90 đến 90.");
        RuleFor(branch => branch.Lng).Must(value => double.IsFinite(value) && value is >= -180 and <= 180)
            .WithMessage("Kinh độ nằm trong khoảng từ -180 đến 180.");
        RuleFor(branch => branch.BankAccountNo).MaximumLength(50);
        RuleFor(branch => branch.BankCode).MaximumLength(20);
        RuleFor(branch => branch.SepayAccountRef).MaximumLength(100);
    }
}
