using Bepthaymao.Application.Abstractions.Messaging;

namespace Bepthaymao.Application.Branches.ListPublicBranches;

public sealed record ListPublicBranchesQuery : IQuery<IReadOnlyList<PublicBranch>>;

public interface IPublicBranchReader
{
    Task<IReadOnlyList<PublicBranch>> ListActiveAsync(CancellationToken cancellationToken);
}

internal sealed class ListPublicBranchesQueryHandler(IPublicBranchReader reader)
    : IQueryHandler<ListPublicBranchesQuery, IReadOnlyList<PublicBranch>>
{
    public Task<IReadOnlyList<PublicBranch>> Handle(ListPublicBranchesQuery query, CancellationToken cancellationToken) =>
        reader.ListActiveAsync(cancellationToken);
}
