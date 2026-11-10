using Bepthaymao.Domain.Common;
using Bepthaymao.Domain.Owners;

namespace Bepthaymao.Domain.Tests.Owners;

public sealed class OwnerTests
{
    [Fact]
    public void CreatePreservesValidOwnerAndUtcTimestamp()
    {
        Guid id = Guid.CreateVersion7();
        DateTimeOffset createdAt = new(2026, 11, 10, 12, 0, 0, TimeSpan.FromHours(7));
        Owner owner = Owner.Create(id, "  Owner  ", createdAt);
        Assert.Equal(id, owner.Id);
        Assert.Equal("Owner", owner.Name);
        Assert.Equal(createdAt.ToUniversalTime(), owner.CreatedAt);
    }

    [Fact]
    public void CreateRejectsEmptyId() => AssertInvalid(Guid.Empty, "Owner");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateRejectsMissingName(string? name) => AssertInvalid(Guid.CreateVersion7(), name!);

    [Fact]
    public void CreateRejectsLongName() => AssertInvalid(Guid.CreateVersion7(), new string('x', 201));

    private static void AssertInvalid(Guid id, string name)
    {
        Assert.Throws<DomainRuleException>(() => Owner.Create(id, name, DateTimeOffset.UtcNow));
    }
}
