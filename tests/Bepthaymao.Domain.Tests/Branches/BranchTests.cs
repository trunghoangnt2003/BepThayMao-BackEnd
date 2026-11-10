using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Tests.Branches;

public sealed class BranchTests
{
    private static readonly Guid _id = Guid.CreateVersion7();
    private static readonly TimeOnly _open = new(6, 0);
    private static readonly TimeOnly _close = new(21, 30);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankNameIsRejected(string name) => AssertRule("name", () => Create(name: name));

    [Fact]
    public void NameLongerThan200IsRejected() => AssertRule("name", () => Create(name: new string('a', 201)));

    [Fact]
    public void BlankAddressIsRejected() => AssertRule("address", () => Create(address: " "));

    [Fact]
    public void AddressLongerThan500IsRejected() => AssertRule("address", () => Create(address: new string('a', 501)));

    [Theory]
    [InlineData("0901 234 567", "0901234567")]
    [InlineData("0901.234.567", "0901234567")]
    [InlineData("+84901234567", "+84901234567")]
    public void PhoneIsNormalized(string input, string expected) => Create(phone: input).Phone.Should().Be(expected);

    [Theory]
    [InlineData("12345")]
    [InlineData("09012345678901")]
    public void InvalidPhoneIsRejected(string phone) => AssertRule("phone", () => Create(phone: phone));

    [Theory]
    [InlineData(21, 30, 6, 0)]
    [InlineData(6, 0, 6, 0)]
    public void CloseTimeMustBeAfterOpenTime(int openHour, int openMinute, int closeHour, int closeMinute)
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(() =>
            Create(open: new TimeOnly(openHour, openMinute), close: new TimeOnly(closeHour, closeMinute)));
        exception.Rule.Should().Be("openTime");
        exception.Message.Should().Be("Giờ đóng phải sau giờ mở trong cùng một ngày.");
    }

    [Fact]
    public void EmptyIdIsRejected() => AssertRule("id", () => Create(id: Guid.Empty));

    [Fact]
    public void CreateTrimsTextAndStoresUtc()
    {
        Branch branch = Create(name: "  Cơ sở Mẫu 1 ", address: " Số 1 ");
        branch.Name.Should().Be("Cơ sở Mẫu 1");
        branch.Address.Should().Be("Số 1");
        branch.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        branch.CreatedAt.Hour.Should().Be(2);
    }

    [Fact]
    public void ApplyDetailsReportsWhetherAnythingChanged()
    {
        Branch branch = Create();
        branch.ApplyDetails("Cơ sở Mẫu 1", "Số 1 Đường Mẫu", "0901 234 567", _open, _close, true).Should().BeFalse();
        branch.ApplyDetails("Cơ sở Mới", "Số 1 Đường Mẫu", "0901234567", _open, _close, false).Should().BeTrue();
        branch.Name.Should().Be("Cơ sở Mới");
        branch.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ApplyDetailsRejectsInvalidValuesWithoutChangingState()
    {
        Branch branch = Create();
        AssertRule("phone", () => branch.ApplyDetails("Cơ sở Mới", "Số 1", "123", _open, _close, true));
        branch.Name.Should().Be("Cơ sở Mẫu 1");
    }

    private static Branch Create(string name = "Cơ sở Mẫu 1", string address = "Số 1 Đường Mẫu",
        string phone = "0901234567", TimeOnly? open = null, TimeOnly? close = null, Guid? id = null) =>
        Branch.Create(id ?? _id, name, address, phone, open ?? _open, close ?? _close, true,
            new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(7)));

    private static void AssertRule(string rule, Action action) =>
        Assert.Throws<DomainRuleException>(action).Rule.Should().Be(rule);
}
