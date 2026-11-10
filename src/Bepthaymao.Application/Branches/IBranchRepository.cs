using Bepthaymao.Domain.Branches;

namespace Bepthaymao.Application.Branches;

public interface IBranchRepository
{
    Task<Branch?> FindAsync(Guid id, CancellationToken cancellationToken);
    void Add(Branch branch);
}
