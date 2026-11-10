namespace Bepthaymao.Application.Branches.ListPublicBranches;

public interface IPublicBranchReader
{
    Task<IReadOnlyList<PublicBranch>> ListActiveAsync(CancellationToken cancellationToken);
}
