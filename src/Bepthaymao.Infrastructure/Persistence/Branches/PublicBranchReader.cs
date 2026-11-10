using System.Globalization;
using Bepthaymao.Application.Branches.ListPublicBranches;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Persistence.Branches;

internal sealed class PublicBranchReader(AppDbContext dbContext) : IPublicBranchReader
{
    public async Task<IReadOnlyList<PublicBranch>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.Branches.AsNoTracking().Where(branch => branch.IsActive)
            .OrderBy(branch => branch.Name).ThenBy(branch => branch.Id)
            .Select(branch => new { branch.Id, branch.Name, branch.Address, branch.Phone, branch.OpenTime, branch.CloseTime })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        // Format in memory so the HH:mm contract never depends on SQL translation.
        return [.. rows.Select(row => new PublicBranch
        {
            Id = row.Id,
            Name = row.Name,
            Address = row.Address,
            Phone = row.Phone,
            OpenTime = row.OpenTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            CloseTime = row.CloseTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        })];
    }
}
