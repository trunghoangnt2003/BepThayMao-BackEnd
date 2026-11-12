using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Shop;

/// <summary>Banner trang chủ khách do Owner nhập (spec FR-015b).</summary>
public sealed class ShopBanner
{
    public const int TitleMaxLength = 40;
    public const int BodyMaxLength = 80;

    private ShopBanner() { }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = "";
    public string? Body { get; private set; }
    public string? ImageUrl { get; private set; }
    public Guid? LinkDishId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ShopBanner Create(Guid id, Guid ownerId, BannerDetails details, DateTimeOffset at)
    {
        if (id == Guid.Empty || ownerId == Guid.Empty) throw new DomainRuleException("id", "Mã banner không hợp lệ.");
        ShopBanner banner = new() { Id = id, OwnerId = ownerId };
        banner.Apply(details, at);
        return banner;
    }

    public void Apply(BannerDetails details, DateTimeOffset at)
    {
        string title = details.Title?.Trim() ?? "";
        if (title.Length is 0 or > TitleMaxLength) throw new DomainRuleException("title", "Tiêu đề dài từ 1 đến 40 ký tự.");
        string? body = string.IsNullOrWhiteSpace(details.Body) ? null : details.Body.Trim();
        if (body is { Length: > BodyMaxLength }) throw new DomainRuleException("body", "Dòng phụ tối đa 80 ký tự.");
        if (details.SortOrder < 0) throw new DomainRuleException("sortOrder", "Thứ tự là số nguyên từ 0 trở lên.");
        Title = title;
        Body = body;
        ImageUrl = string.IsNullOrWhiteSpace(details.ImageUrl) ? null : details.ImageUrl.Trim();
        LinkDishId = details.LinkDishId;
        SortOrder = details.SortOrder;
        IsActive = details.IsActive;
        UpdatedAt = at.ToUniversalTime();
    }
}

public sealed record BannerDetails(string Title, string? Body, string? ImageUrl, Guid? LinkDishId, int SortOrder, bool IsActive);

/// <summary>Đoạn giới thiệu quán, một dòng mỗi Owner.</summary>
public sealed class ShopAbout
{
    public const int MaxLength = 300;

    private ShopAbout() { }

    public Guid OwnerId { get; private set; }
    public string About { get; private set; } = "";
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ShopAbout Create(Guid ownerId, string about, DateTimeOffset at)
    {
        ShopAbout value = new() { OwnerId = ownerId };
        value.Set(about, at);
        return value;
    }

    public void Set(string about, DateTimeOffset at)
    {
        string trimmed = about?.Trim() ?? "";
        if (trimmed.Length > MaxLength) throw new DomainRuleException("about", "Đoạn giới thiệu tối đa 300 ký tự.");
        About = trimmed;
        UpdatedAt = at.ToUniversalTime();
    }
}
