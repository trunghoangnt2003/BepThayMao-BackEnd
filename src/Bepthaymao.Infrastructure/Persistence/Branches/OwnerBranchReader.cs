using Bepthaymao.Application.Branches.Owner;
using Bepthaymao.Domain.Branches;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Persistence.Branches;

internal sealed class OwnerBranchReader(AppDbContext context) : IOwnerBranchReader
{
    public async Task<IReadOnlyList<OwnerBranchView>> ListAsync(CancellationToken cancellationToken) =>
        await ReadAsync(null, cancellationToken);

    public async Task<OwnerBranchView?> GetAsync(Guid branchId, CancellationToken cancellationToken) =>
        (await ReadAsync(branchId, cancellationToken)).SingleOrDefault();

    private async Task<List<OwnerBranchView>> ReadAsync(Guid? branchId, CancellationToken cancellationToken)
    {
        // Mọi truy vấn trên Branches đi qua filter BranchScope: cơ sở của Owner khác không bao giờ xuất hiện.
        IQueryable<Branch> branches = context.Branches.AsNoTracking();
        if (branchId is { } id) branches = branches.Where(branch => branch.Id == id);
        var rows = await (from branch in branches
                          join user in context.Users.AsNoTracking() on (Guid?)branch.Id equals user.BranchId into managers
                          from manager in managers.DefaultIfEmpty()
                          orderby branch.Name, branch.Id
                          select new { Branch = branch, ManagerIdentifier = manager.UserName }).ToListAsync(cancellationToken);
        return [.. rows.Select(row => new OwnerBranchView(row.Branch.OwnerId, BranchResponse.From(row.Branch, row.ManagerIdentifier)))];
    }
}
