using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Domain.Branches;

namespace Bepthaymao.Application.Branches.UpsertBranches;

internal sealed class UpsertBranchesCommandHandler(IBranchRepository repository, TimeProvider timeProvider)
    : ICommandHandler<UpsertBranchesCommand, UpsertBranchesResult>
{
    public async Task<UpsertBranchesResult> Handle(UpsertBranchesCommand command, CancellationToken cancellationToken)
    {
        int inserted = 0;
        int updated = 0;
        int unchanged = 0;
        foreach (BranchUpsertItem item in command.Branches)
        {
            Branch? branch = await repository.FindAsync(item.Id, cancellationToken).ConfigureAwait(false);
            if (branch is null)
            {
                repository.Add(Branch.Create(item.Id, item.Name, item.Address, item.Phone, item.OpenTime,
                    item.CloseTime, item.IsActive, timeProvider.GetUtcNow()));
                inserted++;
            }
            else if (branch.ApplyDetails(item.Name, item.Address, item.Phone, item.OpenTime, item.CloseTime, item.IsActive))
            {
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        return new UpsertBranchesResult(inserted, updated, unchanged);
    }
}
