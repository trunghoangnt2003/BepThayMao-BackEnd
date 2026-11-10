using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Owners;

public sealed class Owner
{
    private Owner() { }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public static Owner Create(Guid id, string name, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new DomainRuleException("id", "Invalid owner identifier.");
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 200) throw new DomainRuleException("name", "Owner name must contain 1 to 200 characters.");
        return new() { Id = id, Name = name, CreatedAt = createdAt.ToUniversalTime() };
    }
}
