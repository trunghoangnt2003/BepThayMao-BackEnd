using Bepthaymao.Domain.Common;
using System.Reflection;

namespace Bepthaymao.Domain.Tests.Owners;

public sealed class OwnerTests
{
    [Fact]
    public void CreatePreservesValidOwnerAndUtcTimestamp()
    {
        Guid id = Guid.CreateVersion7();
        DateTimeOffset createdAt = new(2026, 11, 10, 12, 0, 0, TimeSpan.FromHours(7));
        object owner = Create(id, "  Owner  ", createdAt);
        Assert.Equal(id, owner.GetType().GetProperty("Id")!.GetValue(owner));
        Assert.Equal("Owner", owner.GetType().GetProperty("Name")!.GetValue(owner));
        Assert.Equal(createdAt.ToUniversalTime(), owner.GetType().GetProperty("CreatedAt")!.GetValue(owner));
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

    private static object Create(Guid id, string name, DateTimeOffset createdAt)
    {
        Type? type = typeof(DomainRuleException).Assembly.GetType("Bepthaymao.Domain.Owners.Owner");
        Assert.NotNull(type);
        return type.GetMethod("Create")!.Invoke(null, [id, name, createdAt])!;
    }

    private static void AssertInvalid(Guid id, string name)
    {
        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => Create(id, name, DateTimeOffset.UtcNow));
        Assert.IsType<DomainRuleException>(exception.InnerException);
    }
}
