using System.Globalization;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Common;
using FluentValidation;

namespace Bepthaymao.Application.Branches.UpsertBranches;

public sealed record BranchUpsertItem(Guid Id, string Name, string Address, string Phone, string OpenTime, string CloseTime, bool IsActive,
    double Lat = 0, double Lng = 0, int AbandonedThresholdMinutes = 10, IReadOnlyList<string>? ZaloRecipientIds = null,
    string? BankAccountNo = null, string? BankCode = null, string? SepayAccountRef = null, string? ManagerIdentifier = null);
public sealed record UpsertBranchesCommand(Guid OwnerId, IReadOnlyList<BranchUpsertItem> Branches) : ICommand<UpsertBranchesResult>;
public sealed record UpsertBranchesResult(int Inserted, int Updated, int Unchanged);

internal sealed class UpsertBranchesCommandValidator : AbstractValidator<UpsertBranchesCommand>
{
    public UpsertBranchesCommandValidator()
    {
        RuleFor(command => command.OwnerId).NotEmpty();
        RuleFor(command => command.Branches).NotEmpty();
        RuleForEach(command => command.Branches).ChildRules(item =>
        {
            item.RuleFor(value => value.Name).NotEmpty().MaximumLength(200).OverridePropertyName("name");
            item.RuleFor(value => value.Address).NotEmpty().MaximumLength(500).OverridePropertyName("address");
            item.RuleFor(value => value.Phone).NotEmpty().OverridePropertyName("phone");
            item.RuleFor(value => value.Id).NotEmpty().OverridePropertyName("id");
            item.RuleFor(value => value.OpenTime).Must(IsTime).OverridePropertyName("openTime");
            item.RuleFor(value => value.CloseTime).Must(IsTime).OverridePropertyName("closeTime");
            item.RuleFor(value => value.Lat).Must(value => double.IsFinite(value) && value is >= -90 and <= 90)
                .WithMessage("Vĩ độ nằm trong khoảng từ -90 đến 90.").OverridePropertyName("lat");
            item.RuleFor(value => value.Lng).Must(value => double.IsFinite(value) && value is >= -180 and <= 180)
                .WithMessage("Kinh độ nằm trong khoảng từ -180 đến 180.").OverridePropertyName("lng");
            item.RuleFor(value => value.AbandonedThresholdMinutes).InclusiveBetween(1, 1440)
                .WithMessage("Nhập số phút là số nguyên, tối thiểu 1.").OverridePropertyName("abandonedThresholdMinutes");
            item.RuleFor(value => value.ZaloRecipientIds).Must(ids => ids is null || (ids.Count <= 20 &&
                ids.All(id => !string.IsNullOrWhiteSpace(id) && id.Trim().Length <= 64) &&
                ids.Select(id => id.Trim()).Distinct(StringComparer.Ordinal).Count() == ids.Count)).OverridePropertyName("zaloRecipientIds");
            item.RuleFor(value => value.BankAccountNo).Must(value => value is null || value.Trim().Length <= 50).OverridePropertyName("bankAccountNo");
            item.RuleFor(value => value.BankCode).Must(value => value is null || value.Trim().Length <= 20).OverridePropertyName("bankCode");
            item.RuleFor(value => value.SepayAccountRef).Must(value => value is null || value.Trim().Length <= 100).OverridePropertyName("sepayAccountRef");
            item.RuleFor(value => value).Custom((value, context) =>
            {
                if (IsTime(value.OpenTime) && IsTime(value.CloseTime) && string.CompareOrdinal(value.OpenTime, value.CloseTime) >= 0)
                    context.AddFailure("closeTime", "Giờ đóng phải sau giờ mở trong cùng một ngày.");
            });
        }).OverridePropertyName("branches");
        RuleFor(command => command.Branches).Custom((items, context) =>
        {
            if (items is null) return;
            HashSet<Guid> ids = [];
            for (int index = 0; index < items.Count; index++)
                if (!ids.Add(items[index].Id)) context.AddFailure($"branches[{index}].id", "Mã cơ sở bị trùng.");
        });
    }

    private static bool IsTime(string value) => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

internal sealed class UpsertBranchesCommandHandler(IBranchRepository repository, TimeProvider clock)
    : ICommandHandler<UpsertBranchesCommand, UpsertBranchesResult>
{
    public async Task<UpsertBranchesResult> Handle(UpsertBranchesCommand command, CancellationToken cancellationToken)
    {
        int inserted = 0, updated = 0, unchanged = 0;
        foreach (BranchUpsertItem item in command.Branches)
        {
            TimeOnly open = TimeOnly.ParseExact(item.OpenTime, "HH:mm", CultureInfo.InvariantCulture);
            TimeOnly close = TimeOnly.ParseExact(item.CloseTime, "HH:mm", CultureInfo.InvariantCulture);
            Branch? branch = await repository.FindAsync(item.Id, cancellationToken);
            if (branch is not null && branch.OwnerId != command.OwnerId)
                throw new DomainRuleException("ownerId", "Cơ sở không thuộc Chủ sở hữu này.");
            BranchDetails details = new(item.Name, item.Address, item.Phone, item.Lat, item.Lng, open, close,
                item.AbandonedThresholdMinutes, item.ZaloRecipientIds ?? [], item.BankAccountNo, item.BankCode, item.SepayAccountRef, item.IsActive);
            if (branch is null)
            {
                repository.Add(Branch.Create(item.Id, command.OwnerId, details, clock.GetUtcNow()));
                inserted++;
            }
            else if (branch.ApplyOwnerDetails(details)) updated++;
            else unchanged++;
        }
        return new(inserted, updated, unchanged);
    }
}
