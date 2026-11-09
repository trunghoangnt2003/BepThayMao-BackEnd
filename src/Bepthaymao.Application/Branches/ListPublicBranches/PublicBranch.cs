using System.ComponentModel.DataAnnotations;

namespace Bepthaymao.Application.Branches.ListPublicBranches;

public sealed record PublicBranch
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required string Phone { get; init; }
    [RegularExpression(@"^\d{2}:\d{2}$")]
    public required string OpenTime { get; init; }
    [RegularExpression(@"^\d{2}:\d{2}$")]
    public required string CloseTime { get; init; }
}
