using System.ComponentModel.DataAnnotations;

namespace Bepthaymao.Application.Branches.ListPublicBranches;

/// <summary>Public branch view; exactly the six contract fields, times as HH:mm.</summary>
public sealed record PublicBranch
{
    public const string TimePattern = @"^([01]\d|2[0-3]):[0-5]\d$";

    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required string Phone { get; init; }
    [RegularExpression(TimePattern)]
    public required string OpenTime { get; init; }
    [RegularExpression(TimePattern)]
    public required string CloseTime { get; init; }
}
