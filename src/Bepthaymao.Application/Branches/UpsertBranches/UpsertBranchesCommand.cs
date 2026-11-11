using System.Globalization;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Domain.Branches;
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
            item.RuleFor(value => value.Name).NotEmpty().MaximumLength(200);
            item.RuleFor(value => value.Address).NotEmpty().MaximumLength(500);
            item.RuleFor(value => value.Phone).NotEmpty();
            item.RuleFor(value => value.Id).NotEmpty();
            item.RuleFor(value => value.OpenTime).Must(IsTime);
            item.RuleFor(value => value.CloseTime).Must(IsTime);
        });
        RuleFor(command => command.Branches).Custom((items, context) =>
        {
            if (items is null) return;
            HashSet<Guid> ids = [];
            for (int index = 0; index < items.Count; index++)
                if (!ids.Add(items[index].Id)) context.AddFailure($"Branches[{index}].Id", "Mã cơ sở bị trùng.");
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
