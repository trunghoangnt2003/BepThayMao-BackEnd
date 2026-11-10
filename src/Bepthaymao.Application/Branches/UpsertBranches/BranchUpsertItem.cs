namespace Bepthaymao.Application.Branches.UpsertBranches;

public sealed record BranchUpsertItem(Guid Id, string Name, string Address, string Phone,
    TimeOnly OpenTime, TimeOnly CloseTime, bool IsActive);
