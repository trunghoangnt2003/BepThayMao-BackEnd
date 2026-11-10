using Bepthaymao.Application.Abstractions.Messaging;

namespace Bepthaymao.Application.Branches.UpsertBranches;

public sealed record UpsertBranchesCommand(IReadOnlyList<BranchUpsertItem> Branches)
    : ICommand<UpsertBranchesResult>;
