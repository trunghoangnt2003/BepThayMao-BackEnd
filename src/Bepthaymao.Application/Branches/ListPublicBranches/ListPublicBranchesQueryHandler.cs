using Bepthaymao.Application.Abstractions.Messaging;

namespace Bepthaymao.Application.Branches.ListPublicBranches;

internal sealed class ListPublicBranchesQueryHandler(IPublicBranchReader reader)
    : IQueryHandler<ListPublicBranchesQuery, IReadOnlyList<PublicBranch>>
{
    public Task<IReadOnlyList<PublicBranch>> Handle(ListPublicBranchesQuery query, CancellationToken cancellationToken) =>
        reader.ListActiveAsync(cancellationToken);
}
