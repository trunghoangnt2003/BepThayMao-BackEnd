using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Security;

namespace Bepthaymao.Application.Branches.Owner.GetOwnerBranch;

public sealed record GetOwnerBranchQuery(Guid BranchId) : IQuery<BranchResponse>;

internal sealed class GetOwnerBranchQueryHandler(ICurrentUser currentUser, IOwnerBranchReader reader)
    : IQueryHandler<GetOwnerBranchQuery, BranchResponse>
{
    public async Task<BranchResponse> Handle(GetOwnerBranchQuery query, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        // Filter BranchScope lọc cơ sở của Owner khác nên kết quả null: khớp 404, không lộ sự tồn tại.
        OwnerBranchView view = await reader.GetAsync(query.BranchId, cancellationToken) ?? throw new NotFoundException();
        if (view.OwnerId != ownerId) throw new ForbiddenException();
        return view.Branch;
    }
}
