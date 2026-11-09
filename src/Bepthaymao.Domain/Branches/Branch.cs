using System.Text.RegularExpressions;
using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Branches;

public sealed partial class Branch
{
    private Branch() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public TimeOnly OpenTime { get; private set; }
    public TimeOnly CloseTime { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Branch Create(Guid id, string name, string address, string phone,
        TimeOnly openTime, TimeOnly closeTime, bool isActive, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new DomainRuleException("id", "Mã cơ sở không hợp lệ.");
        Branch branch = new() { Id = id, CreatedAt = createdAt.ToUniversalTime() };
        branch.ApplyDetails(name, address, phone, openTime, closeTime, isActive);
        return branch;
    }

    public bool ApplyDetails(string name, string address, string phone,
        TimeOnly openTime, TimeOnly closeTime, bool isActive)
    {
        name = name.Trim();
        address = address.Trim();
        phone = string.Concat(phone.Where(character => !char.IsWhiteSpace(character) && character != '.'));
        if (name.Length is 0 or > 200) throw new DomainRuleException("name", "Nhập tên cơ sở (tối đa 200 ký tự).");
        if (address.Length is 0 or > 500) throw new DomainRuleException("address", "Nhập địa chỉ (tối đa 500 ký tự).");
        if (!PhonePattern().IsMatch(phone)) throw new DomainRuleException("phone", "Nhập số điện thoại hợp lệ, ví dụ 0901 234 567.");
        if (openTime >= closeTime) throw new DomainRuleException("openTime", "Giờ đóng phải sau giờ mở trong cùng một ngày.");
        bool changed = Name != name || Address != address || Phone != phone || OpenTime != openTime || CloseTime != closeTime || IsActive != isActive;
        (Name, Address, Phone, OpenTime, CloseTime, IsActive) = (name, address, phone, openTime, closeTime, isActive);
        return changed;
    }

    [GeneratedRegex(@"^(\+84|0)[0-9]{8,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
