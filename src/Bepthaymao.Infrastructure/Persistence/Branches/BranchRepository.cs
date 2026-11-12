using Bepthaymao.Application.Branches;
using Bepthaymao.Domain.Branches;

namespace Bepthaymao.Infrastructure.Persistence.Branches;

internal sealed class BranchRepository(AppDbContext context) : IBranchRepository
{
    public async Task<Branch?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Branches.FindAsync([id], cancellationToken);
    public void Add(Branch branch) => context.Branches.Add(branch);
}
