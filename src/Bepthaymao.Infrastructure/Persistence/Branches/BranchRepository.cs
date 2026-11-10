using Bepthaymao.Application.Branches;
using Bepthaymao.Domain.Branches;

namespace Bepthaymao.Infrastructure.Persistence.Branches;

internal sealed class BranchRepository(AppDbContext dbContext) : IBranchRepository
{
    public Task<Branch?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Branches.FindAsync([id], cancellationToken).AsTask();

    public void Add(Branch branch) => dbContext.Branches.Add(branch);
}
