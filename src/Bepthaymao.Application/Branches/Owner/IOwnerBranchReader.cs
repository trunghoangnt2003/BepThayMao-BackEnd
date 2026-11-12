namespace Bepthaymao.Application.Branches.Owner;

/// <summary>Cơ sở cùng chủ sở hữu thực; <c>OwnerId</c> chỉ để lớp Application kiểm lại, không đưa ra API.</summary>
public sealed record OwnerBranchView(Guid OwnerId, BranchResponse Branch);

/// <summary>Đọc cơ sở của Owner hiện tại; mọi truy vấn đi qua filter <c>BranchScope</c> nên cơ sở của Owner khác không xuất hiện.</summary>
public interface IOwnerBranchReader
{
    Task<IReadOnlyList<OwnerBranchView>> ListAsync(CancellationToken cancellationToken);
    Task<OwnerBranchView?> GetAsync(Guid branchId, CancellationToken cancellationToken);
}
