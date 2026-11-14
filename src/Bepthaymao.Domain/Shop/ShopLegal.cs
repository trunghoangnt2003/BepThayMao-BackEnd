using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Shop;

/// <summary>
/// Thông tin người bán công khai ở chân trang (Nghị định 52/2013, sửa đổi bởi 85/2021): tên, số đăng ký kinh doanh hoặc
/// mã số thuế, ngày và nơi cấp, địa chỉ, điện thoại, email, đường dẫn trang "Đã thông báo Bộ Công Thương". Một bản mỗi Owner.
/// </summary>
public sealed class ShopLegal
{
    public const int NameMaxLength = 200;
    public const int RegistrationMaxLength = 50;
    public const int IssuedByMaxLength = 200;
    public const int AddressMaxLength = 300;
    public const int PhoneMaxLength = 20;
    public const int EmailMaxLength = 120;
    public const int NoticeUrlMaxLength = 500;
    public const string NoticeHost = "online.gov.vn";

    private ShopLegal() { }

    public Guid OwnerId { get; private set; }
    public string? BusinessName { get; private set; }
    public string? RegistrationNumber { get; private set; }
    public DateOnly? RegistrationIssuedOn { get; private set; }
    public string? RegistrationIssuedBy { get; private set; }
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? MoitNoticeUrl { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ShopLegal Create(Guid ownerId, LegalDetails details, DateTimeOffset at)
    {
        if (ownerId == Guid.Empty) throw new DomainRuleException("ownerId", "Mã chủ quán không hợp lệ.");
        ShopLegal legal = new() { OwnerId = ownerId };
        legal.Apply(details, at);
        return legal;
    }

    public void Apply(LegalDetails details, DateTimeOffset at)
    {
        BusinessName = Optional(details.BusinessName, NameMaxLength, "businessName", "Tên người bán");
        RegistrationNumber = Optional(details.RegistrationNumber, RegistrationMaxLength, "registrationNumber", "Số đăng ký");
        RegistrationIssuedOn = details.RegistrationIssuedOn;
        RegistrationIssuedBy = Optional(details.RegistrationIssuedBy, IssuedByMaxLength, "registrationIssuedBy", "Nơi cấp");
        Address = Optional(details.Address, AddressMaxLength, "address", "Địa chỉ");
        Phone = Optional(details.Phone, PhoneMaxLength, "phone", "Số điện thoại");
        Email = Optional(details.Email, EmailMaxLength, "email", "Email");
        MoitNoticeUrl = NoticeUrl(details.MoitNoticeUrl);
        UpdatedAt = at.ToUniversalTime();
    }

    private static string? Optional(string? value, int max, string field, string label)
    {
        string? trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return trimmed is { Length: var length } && length > max
            ? throw new DomainRuleException(field, $"{label} tối đa {max} ký tự.")
            : trimmed;
    }

    /// <summary>Chỉ nhận đường dẫn https tới online.gov.vn để biểu tượng không dẫn khách ra trang lạ.</summary>
    private static string? NoticeUrl(string? value)
    {
        string? trimmed = Optional(value, NoticeUrlMaxLength, "moitNoticeUrl", "Đường dẫn thông báo");
        if (trimmed is null) return null;
        bool valid = Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host == NoticeHost || uri.Host.EndsWith("." + NoticeHost, StringComparison.Ordinal));
        return valid ? trimmed : throw new DomainRuleException("moitNoticeUrl", "Dán đường dẫn https từ online.gov.vn.");
    }
}

public sealed record LegalDetails(string? BusinessName, string? RegistrationNumber, DateOnly? RegistrationIssuedOn,
    string? RegistrationIssuedBy, string? Address, string? Phone, string? Email, string? MoitNoticeUrl);

/// <summary>Sáu chính sách website bán hàng cần công khai.</summary>
public enum PolicyKind
{
    Terms,
    Privacy,
    Delivery,
    Payment,
    Refund,
    Complaint,
}

/// <summary>Nội dung một chính sách do chủ quán viết; chưa viết thì không hiện ở chân trang.</summary>
public sealed class ShopPolicy
{
    public const int BodyMaxLength = 20000;

    private ShopPolicy() { }

    public Guid OwnerId { get; private set; }
    public PolicyKind Kind { get; private set; }
    public string Body { get; private set; } = "";
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ShopPolicy Create(Guid ownerId, PolicyKind kind, string body, DateTimeOffset at)
    {
        if (ownerId == Guid.Empty) throw new DomainRuleException("ownerId", "Mã chủ quán không hợp lệ.");
        if (!Enum.IsDefined(kind)) throw new DomainRuleException("kind", "Loại chính sách không hợp lệ.");
        ShopPolicy policy = new() { OwnerId = ownerId, Kind = kind };
        policy.Set(body, at);
        return policy;
    }

    public void Set(string body, DateTimeOffset at)
    {
        string trimmed = body?.Trim() ?? "";
        if (trimmed.Length > BodyMaxLength) throw new DomainRuleException("body", "Nội dung chính sách tối đa 20.000 ký tự.");
        Body = trimmed;
        UpdatedAt = at.ToUniversalTime();
    }
}
