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
        Assert.False(branch.ApplyOwnerDetails(Details("Mẫu", "Địa chỉ", expected)));
        Assert.True(branch.ApplyOwnerDetails(Details("Đổi tên", "Địa chỉ", expected) with { IsActive = false }));
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
        Assert.Equal("id", Assert.Throws<DomainRuleException>(() => Branch.Create(Guid.Empty, Guid.CreateVersion7(), Details(), DateTimeOffset.UtcNow)).Rule);
        Assert.Equal("name", Assert.Throws<DomainRuleException>(() => Create(name: new string('a', 201))).Rule);
        Assert.Equal("address", Assert.Throws<DomainRuleException>(() => Create(address: new string('a', 501))).Rule);
        Branch branch = Create();
        Assert.Equal("openTime", Assert.Throws<DomainRuleException>(() => branch.ApplyOwnerDetails(Details() with { OpenTime = new(21, 30), CloseTime = new(6, 0) })).Rule);
        Assert.Equal("openTime", Assert.Throws<DomainRuleException>(() => branch.ApplyOwnerDetails(Details() with { CloseTime = new(6, 0) })).Rule);
        Assert.Equal(new TimeOnly(6, 0), branch.OpenTime);
    }

    [Theory]
    [InlineData(90.0001, 106, "lat")]
    [InlineData(10, -180.0001, "lng")]
    [InlineData(double.NaN, 106, "lat")]
    [InlineData(10, double.PositiveInfinity, "lng")]
    public void RejectsInvalidCoordinates(double lat, double lng, string rule)
    {
        Assert.Equal(rule, Assert.Throws<DomainRuleException>(() => CreateWith(Details() with { Lat = lat, Lng = lng })).Rule);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void RejectsInvalidAbandonedThreshold(int threshold)
    {
        Assert.Equal("abandonedThresholdMinutes", Assert.Throws<DomainRuleException>(() => CreateWith(Details() with { AbandonedThresholdMinutes = threshold })).Rule);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public void AcceptsThresholdBoundaryAndUnchangedSettings(int threshold)
    {
        BranchDetails details = Details() with { AbandonedThresholdMinutes = threshold };
        Branch branch = CreateWith(details);
        Assert.False(branch.ApplyOwnerDetails(details));
    }

    [Fact]
    public void RejectsInvalidZaloRecipientsAfterTrimming()
    {
        foreach (string[] ids in new[] { new[] { " " }, [new string('a', 65)], Enumerable.Range(0, 21).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray() })
            Assert.Equal("zaloRecipientIds", Assert.Throws<DomainRuleException>(() => CreateWith(Details() with { ZaloRecipientIds = ids })).Rule);
        DomainRuleException duplicate = Assert.Throws<DomainRuleException>(() => CreateWith(Details() with { ZaloRecipientIds = [" id ", "id"] }));
        Assert.Equal("ID Zalo bị trùng: id.", duplicate.Message);
    }

    [Fact]
    public void NormalizesBankFieldsAndRejectsExcessLengthsWithoutChangingExistingState()
    {
        Branch branch = CreateWith(Details() with { BankAccountNo = " ", BankCode = " ", SepayAccountRef = " " });
        Assert.Null(branch.BankAccountNo);
        Assert.Null(branch.BankCode);
        Assert.Null(branch.SepayAccountRef);
        Assert.Equal("bankAccountNo", Assert.Throws<DomainRuleException>(() => branch.ApplyOwnerDetails(Details() with { BankAccountNo = new('1', 51), Name = "Changed" })).Rule);
        Assert.Equal("bankCode", Assert.Throws<DomainRuleException>(() => branch.ApplyOwnerDetails(Details() with { BankCode = new('a', 21) })).Rule);
        Assert.Equal("sepayAccountRef", Assert.Throws<DomainRuleException>(() => branch.ApplyOwnerDetails(Details() with { SepayAccountRef = new('a', 101) })).Rule);
        Assert.Equal("Mẫu", branch.Name);
    }

    private static Branch CreateWith(BranchDetails details) => Branch.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), details, DateTimeOffset.UtcNow);

    private static Branch Create(string name = " Mẫu ", string address = " Địa chỉ ", string phone = "0901234567") =>
        Branch.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Details(name, address, phone), DateTimeOffset.Now);

    private static BranchDetails Details(string name = "Mẫu", string address = "Địa chỉ", string phone = "0901234567") =>
        new(name, address, phone, 10, 106, new(6, 0), new(21, 30), 10, [], null, null, null, true);
}
