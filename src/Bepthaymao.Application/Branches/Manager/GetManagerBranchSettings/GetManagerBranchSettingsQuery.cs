using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Domain.Branches;

namespace Bepthaymao.Application.Branches.Manager.GetManagerBranchSettings;

/// <summary>Không có tham số: cơ sở lấy từ claim <c>branch_id</c>, không bao giờ từ yêu cầu.</summary>
public sealed record GetManagerBranchSettingsQuery : IQuery<ManagerBranchView>;

internal sealed class GetManagerBranchSettingsQueryHandler(ICurrentUser currentUser, IBranchRepository repository)
    : IQueryHandler<GetManagerBranchSettingsQuery, ManagerBranchView>
{
    public async Task<ManagerBranchView> Handle(GetManagerBranchSettingsQuery query, CancellationToken cancellationToken)
    {
        Guid branchId = currentUser.BranchId ?? throw new ForbiddenException();
        Branch branch = await repository.FindAsync(branchId, cancellationToken) ?? throw new NotFoundException();
        // Lớp 1: kiểm lại khi filter EF bị bỏ qua nhầm; thà từ chối còn hơn trả cơ sở khác.
        if (branch.Id != branchId) throw new ForbiddenException();
        return ManagerBranchView.From(branch);
    }
}
