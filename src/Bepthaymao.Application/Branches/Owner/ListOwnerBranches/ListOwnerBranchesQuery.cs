using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Security;

namespace Bepthaymao.Application.Branches.Owner.ListOwnerBranches;

public sealed record ListOwnerBranchesQuery : IQuery<IReadOnlyList<BranchResponse>>;

internal sealed class ListOwnerBranchesQueryHandler(ICurrentUser currentUser, IOwnerBranchReader reader)
    : IQueryHandler<ListOwnerBranchesQuery, IReadOnlyList<BranchResponse>>
{
    public async Task<IReadOnlyList<BranchResponse>> Handle(ListOwnerBranchesQuery query, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        IReadOnlyList<OwnerBranchView> views = await reader.ListAsync(cancellationToken);
        // Lớp 1: kiểm lại khi filter EF bị bỏ qua nhầm; thà từ chối còn hơn trả cơ sở của Owner khác.
        if (views.Any(view => view.OwnerId != ownerId)) throw new ForbiddenException();
        return [.. views.Select(view => view.Branch)];
    }
}
