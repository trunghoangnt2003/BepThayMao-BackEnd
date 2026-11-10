using System.Text.RegularExpressions;
using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Branches;

public sealed partial class Branch
{
    public const int NameMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int PhoneMaxLength = 20;

    private Branch() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public TimeOnly OpenTime { get; private set; }
    public TimeOnly CloseTime { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Branch Create(Guid id, string name, string address, string phone, TimeOnly openTime,
        TimeOnly closeTime, bool isActive, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleException("id", "Mã cơ sở không hợp lệ.");
        }

        Branch branch = new() { Id = id, CreatedAt = createdAt.ToUniversalTime() };
        branch.ApplyDetails(name, address, phone, openTime, closeTime, isActive);
        return branch;
    }

    /// <returns><see langword="true"/> when any stored value changed.</returns>
    public bool ApplyDetails(string name, string address, string phone, TimeOnly openTime,
        TimeOnly closeTime, bool isActive)
    {
        string normalizedName = Required(name, "name", NameMaxLength, "Nhập tên cơ sở.");
        string normalizedAddress = Required(address, "address", AddressMaxLength, "Nhập địa chỉ.");
        string normalizedPhone = NormalizePhone(phone);
        if (openTime >= closeTime)
        {
            throw new DomainRuleException("openTime", "Giờ đóng phải sau giờ mở trong cùng một ngày.");
        }

        bool changed = Name != normalizedName || Address != normalizedAddress || Phone != normalizedPhone
            || OpenTime != openTime || CloseTime != closeTime || IsActive != isActive;
        Name = normalizedName;
        Address = normalizedAddress;
        Phone = normalizedPhone;
        OpenTime = openTime;
        CloseTime = closeTime;
        IsActive = isActive;
        return changed;
    }

    public static string NormalizePhone(string phone)
    {
        string normalized = (phone ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);
        return PhonePattern().IsMatch(normalized)
            ? normalized
            : throw new DomainRuleException("phone", "Nhập số điện thoại hợp lệ, ví dụ 0901 234 567.");
    }

    private static string Required(string value, string rule, int maximumLength, string message)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length == 0 || normalized.Length > maximumLength
            ? throw new DomainRuleException(rule, message)
            : normalized;
    }

    // UI-SPEC A4: +84 or 0 followed by 8-10 digits, after stripping spaces and dots.
    [GeneratedRegex(@"^(\+84|0)[0-9]{8,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
