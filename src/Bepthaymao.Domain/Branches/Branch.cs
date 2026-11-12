using System.Text.RegularExpressions;
using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Branches;

public sealed partial class Branch
{
    private Branch() { }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public int AbandonedThresholdMinutes { get; private set; }
    public List<string> ZaloRecipientIds { get; private set; } = [];
    public string? BankAccountNo { get; private set; }
    public string? BankCode { get; private set; }
    public string? SepayAccountRef { get; private set; }
    public string Name { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public TimeOnly OpenTime { get; private set; }
    public TimeOnly CloseTime { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Branch Create(Guid id, Guid ownerId, BranchDetails details, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new DomainRuleException("id", "Mã cơ sở không hợp lệ.");
        if (ownerId == Guid.Empty) throw new DomainRuleException("ownerId", "Mã chủ sở hữu không hợp lệ.");
        Branch branch = new() { Id = id, OwnerId = ownerId, CreatedAt = createdAt.ToUniversalTime() };
        branch.ApplyOwnerDetails(details);
        return branch;
    }

    public bool ApplyOwnerDetails(BranchDetails details)
    {
        (string name, string address, string phone, double lat, double lng, TimeOnly openTime, TimeOnly closeTime,
            int abandonedThresholdMinutes, IReadOnlyList<string> zaloRecipientIds, string? bankAccountNo,
            string? bankCode, string? sepayAccountRef, bool isActive) = details;
        name = name.Trim();
        address = address.Trim();
        phone = string.Concat(phone.Where(character => !char.IsWhiteSpace(character) && character != '.'));
        if (name.Length is 0 or > 200) throw new DomainRuleException("name", "Nhập tên cơ sở (tối đa 200 ký tự).");
        if (address.Length is 0 or > 500) throw new DomainRuleException("address", "Nhập địa chỉ (tối đa 500 ký tự).");
        if (!PhonePattern().IsMatch(phone)) throw new DomainRuleException("phone", "Nhập số điện thoại hợp lệ, ví dụ 0901 234 567.");
        if (openTime >= closeTime) throw new DomainRuleException("openTime", "Giờ đóng phải sau giờ mở trong cùng một ngày.");
        if (!double.IsFinite(lat) || lat is < -90 or > 90) throw new DomainRuleException("lat", "Vĩ độ nằm trong khoảng từ -90 đến 90.");
        if (!double.IsFinite(lng) || lng is < -180 or > 180) throw new DomainRuleException("lng", "Kinh độ nằm trong khoảng từ -180 đến 180.");
        if (abandonedThresholdMinutes is < 1 or > 1440) throw new DomainRuleException("abandonedThresholdMinutes", "Nhập số phút là số nguyên, tối thiểu 1.");
        string[] normalizedIds = zaloRecipientIds.Select(value => value?.Trim() ?? "").ToArray();
        if (normalizedIds.Length > 20 || normalizedIds.Any(value => value.Length is 0 or > 64))
            throw new DomainRuleException("zaloRecipientIds", "Nhập tối đa 20 ID Zalo, mỗi ID từ 1 đến 64 ký tự.");
        HashSet<string> uniqueIds = new(StringComparer.Ordinal);
        foreach (string value in normalizedIds)
            if (!uniqueIds.Add(value)) throw new DomainRuleException("zaloRecipientIds", $"ID Zalo bị trùng: {value}.");
        bankAccountNo = NormalizeOptional(bankAccountNo, 50, "bankAccountNo");
        bankCode = NormalizeOptional(bankCode, 20, "bankCode");
        sepayAccountRef = NormalizeOptional(sepayAccountRef, 100, "sepayAccountRef");
        bool changed = Name != name || Address != address || Phone != phone || OpenTime != openTime || CloseTime != closeTime || IsActive != isActive ||
            Lat != lat || Lng != lng || AbandonedThresholdMinutes != abandonedThresholdMinutes ||
            !ZaloRecipientIds.SequenceEqual(normalizedIds) || BankAccountNo != bankAccountNo || BankCode != bankCode || SepayAccountRef != sepayAccountRef;
        (Name, Address, Phone, OpenTime, CloseTime, IsActive) = (name, address, phone, openTime, closeTime, isActive);
        (Lat, Lng, AbandonedThresholdMinutes, ZaloRecipientIds, BankAccountNo, BankCode, SepayAccountRef) =
            (lat, lng, abandonedThresholdMinutes, [.. normalizedIds], bankAccountNo, bankCode, sepayAccountRef);
        return changed;
    }

    // Chỗ giữ chữ ký để test RED biên dịch được; hiện thực thật ở commit feat kế tiếp.
    public bool ApplyManagerSettings(string phone, TimeOnly openTime, TimeOnly closeTime, int abandonedThresholdMinutes,
        IReadOnlyList<string> zaloRecipientIds) => Phone.Length < 0;

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > maxLength) throw new DomainRuleException(field, $"Nhập tối đa {maxLength} ký tự.");
        return value;
    }

    [GeneratedRegex(@"^(\+84|0)[0-9]{8,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
