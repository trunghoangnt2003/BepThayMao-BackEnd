using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Tests.Branches;

public sealed class BranchTests
{
    [Theory]
    [InlineData("0901 234 567", "0901234567")]
    [InlineData("0901.234.567", "0901234567")]
    [InlineData("+84901234567", "+84901234567")]
    public void CreatesNormalizedBranchAndPreservesUtc(string phone, string expected)
    {
        Branch branch = Create(phone: phone);
        Assert.Equal(expected, branch.Phone);
        Assert.Equal("Mẫu", branch.Name);
        Assert.Equal(TimeSpan.Zero, branch.CreatedAt.Offset);
        Assert.False(branch.ApplyDetails("Mẫu", "Địa chỉ", expected, new(6, 0), new(21, 30), true));
        Assert.True(branch.ApplyDetails("Đổi tên", "Địa chỉ", expected, new(6, 0), new(21, 30), false));
        Assert.False(branch.IsActive);
    }

    [Theory]
    [InlineData("", "Địa chỉ", "0901234567", "name")]
    [InlineData("  ", "Địa chỉ", "0901234567", "name")]
    [InlineData("Mẫu", "", "0901234567", "address")]
    [InlineData("Mẫu", "Địa chỉ", "12345", "phone")]
    [InlineData("Mẫu", "Địa chỉ", "09012345678901", "phone")]
    public void RejectsInvalidDetails(string name, string address, string phone, string rule)
    {
        DomainRuleException error = Assert.Throws<DomainRuleException>(() => Create(name, address, phone));
        Assert.Equal(rule, error.Rule);
    }

    [Fact]
    public void RejectsEmptyIdLongFieldsAndInvalidHours()
    {
        Assert.Equal("id", Assert.Throws<DomainRuleException>(() => Branch.Create(Guid.Empty, "Mẫu", "Địa chỉ", "0901234567", new(6, 0), new(21, 30), true, DateTimeOffset.UtcNow)).Rule);
        Assert.Equal("name", Assert.Throws<DomainRuleException>(() => Create(name: new string('a', 201))).Rule);
        Assert.Equal("address", Assert.Throws<DomainRuleException>(() => Create(address: new string('a', 501))).Rule);
        Branch branch = Create();
        Assert.Equal("openTime", Assert.Throws<DomainRuleException>(() => branch.ApplyDetails("Mẫu", "Địa chỉ", "0901234567", new(21, 30), new(6, 0), true)).Rule);
        Assert.Equal("openTime", Assert.Throws<DomainRuleException>(() => branch.ApplyDetails("Mẫu", "Địa chỉ", "0901234567", new(6, 0), new(6, 0), true)).Rule);
        Assert.Equal(new TimeOnly(6, 0), branch.OpenTime);
    }

    private static Branch Create(string name = " Mẫu ", string address = " Địa chỉ ", string phone = "0901234567") =>
        Branch.Create(Guid.CreateVersion7(), name, address, phone, new(6, 0), new(21, 30), true, DateTimeOffset.Now);
}
