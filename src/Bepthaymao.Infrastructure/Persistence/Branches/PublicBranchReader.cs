using System.Globalization;
using Bepthaymao.Application.Branches.ListPublicBranches;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Persistence.Branches;

internal sealed class PublicBranchReader(AppDbContext context) : IPublicBranchReader
{
    public async Task<IReadOnlyList<PublicBranch>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var branches = await context.Branches.AsNoTracking().Where(branch => branch.IsActive)
            .OrderBy(branch => branch.Name).ThenBy(branch => branch.Id)
            .Select(branch => new { branch.Id, branch.Name, branch.Address, branch.Phone, branch.OpenTime, branch.CloseTime })
            .ToListAsync(cancellationToken);
        return branches.Select(branch => new PublicBranch
        {
            Id = branch.Id,
            Name = branch.Name,
            Address = branch.Address,
            Phone = branch.Phone,
            OpenTime = branch.OpenTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            CloseTime = branch.CloseTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        }).ToArray();
    }
}
