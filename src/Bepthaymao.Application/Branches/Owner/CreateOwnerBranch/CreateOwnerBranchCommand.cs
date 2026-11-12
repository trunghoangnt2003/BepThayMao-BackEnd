using System.Globalization;
using System.Text.RegularExpressions;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Domain.Branches;
using FluentValidation;

namespace Bepthaymao.Application.Branches.Owner.CreateOwnerBranch;

public sealed record BranchOwnerSettingsData(string Name, string Address, string Phone, double Lat, double Lng, string OpenTime,
    string CloseTime, int AbandonedThresholdMinutes, IReadOnlyList<string> ZaloRecipientIds, string? BankAccountNo,
    string? BankCode, string? SepayAccountRef, bool IsActive);
public sealed record ManagerAccountData(string Identifier, string Password);
public sealed record CreateOwnerBranchCommand(BranchOwnerSettingsData Branch, ManagerAccountData Manager) : ICommand<BranchResponse>;

internal sealed partial class CreateOwnerBranchCommandValidator : AbstractValidator<CreateOwnerBranchCommand>
{
    public CreateOwnerBranchCommandValidator()
    {
        RuleFor(command => command.Branch).NotNull().WithMessage("Thiếu thông tin cơ sở.");
        RuleFor(command => command.Manager).NotNull().WithMessage("Thiếu thông tin tài khoản quản lý.");
        When(command => command.Branch is not null, () =>
        {
            RuleFor(command => command.Branch.Name).NotEmpty().MaximumLength(200).WithMessage("Nhập tên cơ sở (tối đa 200 ký tự).");
            RuleFor(command => command.Branch.Address).NotEmpty().MaximumLength(500).WithMessage("Nhập địa chỉ (tối đa 500 ký tự).");
            RuleFor(command => command.Branch.Phone).Must(phone => phone is not null && PhonePattern().IsMatch(StripPhone(phone)))
                .WithMessage("Nhập số điện thoại hợp lệ, ví dụ 0901 234 567.");
            RuleFor(command => command.Branch.OpenTime).Must(IsTime).WithMessage("Nhập giờ mở theo dạng HH:mm.");
            RuleFor(command => command.Branch.CloseTime).Must(IsTime).WithMessage("Nhập giờ đóng theo dạng HH:mm.");
            RuleFor(command => command.Branch).Custom((branch, context) =>
            {
                if (IsTime(branch.OpenTime) && IsTime(branch.CloseTime) && string.CompareOrdinal(branch.OpenTime, branch.CloseTime) >= 0)
                    context.AddFailure("Branch.OpenTime", "Giờ đóng phải sau giờ mở trong cùng một ngày.");
            });
            RuleFor(command => command.Branch.Lat).Must(value => double.IsFinite(value) && value is >= -90 and <= 90)
                .WithMessage("Vĩ độ nằm trong khoảng từ -90 đến 90.");
            RuleFor(command => command.Branch.Lng).Must(value => double.IsFinite(value) && value is >= -180 and <= 180)
                .WithMessage("Kinh độ nằm trong khoảng từ -180 đến 180.");
            RuleFor(command => command.Branch.AbandonedThresholdMinutes).InclusiveBetween(1, 1440)
                .WithMessage("Nhập số phút là số nguyên từ 1 đến 1440.");
            RuleFor(command => command.Branch.ZaloRecipientIds).Must(ids => ids is not null && ids.Count <= 20
                    && ids.All(id => id is not null && id.Trim().Length is >= 1 and <= 64)
                    && ids.Select(id => id.Trim()).Distinct(StringComparer.Ordinal).Count() == ids.Count)
                .WithMessage("Nhập tối đa 20 ID Zalo không trùng, mỗi ID từ 1 đến 64 ký tự.");
            RuleFor(command => command.Branch.BankAccountNo).MaximumLength(50);
            RuleFor(command => command.Branch.BankCode).MaximumLength(20);
            RuleFor(command => command.Branch.SepayAccountRef).MaximumLength(100);
        });
        When(command => command.Manager is not null, () =>
        {
            RuleFor(command => command.Manager.Identifier).Must(identifier => identifier is not null && IdentifierPattern().IsMatch(identifier))
                .WithMessage("Tên đăng nhập gồm 3 đến 64 ký tự: chữ, số và . _ @ + -");
            RuleFor(command => command.Manager.Password).NotEmpty().MinimumLength(10).MaximumLength(128)
                .WithMessage("Mật khẩu phải từ 10 đến 128 ký tự.");
        });
    }

    private static string StripPhone(string phone) => string.Concat(phone.Where(character => !char.IsWhiteSpace(character) && character != '.'));

    private static bool IsTime(string? value) => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    [GeneratedRegex(@"^(\+84|0)[0-9]{8,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[A-Za-z0-9._@+-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}

internal sealed class CreateOwnerBranchCommandHandler(ICurrentUser currentUser, IBranchRepository repository,
    IManagerAccountService managers, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateOwnerBranchCommand, BranchResponse>
{
    public async Task<BranchResponse> Handle(CreateOwnerBranchCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        BranchOwnerSettingsData data = command.Branch;
        BranchDetails details = new(data.Name, data.Address, data.Phone, data.Lat, data.Lng,
            TimeOnly.ParseExact(data.OpenTime, "HH:mm", CultureInfo.InvariantCulture),
            TimeOnly.ParseExact(data.CloseTime, "HH:mm", CultureInfo.InvariantCulture),
            data.AbandonedThresholdMinutes, data.ZaloRecipientIds, data.BankAccountNo, data.BankCode, data.SepayAccountRef, data.IsActive);
        Branch branch = Branch.Create(Guid.CreateVersion7(), ownerId, details, clock.GetUtcNow());
        string? managerIdentifier = null;
        // UserManager tự gọi SaveChanges trên cùng DbContext: cơ sở, user và user_roles cùng nằm trong transaction này.
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            repository.Add(branch);
            managerIdentifier = await managers.CreateManagerAsync(branch.Id, command.Manager.Identifier, command.Manager.Password, token);
            await unitOfWork.SaveChangesAsync(token);
        }, cancellationToken);
        return BranchResponse.From(branch, managerIdentifier);
    }
}
