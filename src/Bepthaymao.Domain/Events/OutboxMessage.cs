namespace Bepthaymao.Domain.Events;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ClaimedUntil { get; set; }
    public int Attempts { get; set; }
}

public sealed class InboxMessage
{
    public string Consumer { get; set; } = "";
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class OperationalAlert
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public Guid? BranchId { get; set; }
    public Guid? OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
