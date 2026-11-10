using Bepthaymao.Application.Abstractions.Messaging;

namespace Bepthaymao.Application.Branches.ListPublicBranches;

public sealed record ListPublicBranchesQuery : IQuery<IReadOnlyList<PublicBranch>>;
