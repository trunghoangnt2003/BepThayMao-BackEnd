using Bepthaymao.Domain.Common;

namespace Bepthaymao.Domain.Menu;

/// <summary>Nhóm món hiển thị cho khách.</summary>
public enum DishCategory
{
    XoiMan,
    XoiNgot,
    Chay,
}

/// <summary>Món của thực đơn chung do Owner quản lý; giá giống nhau ở mọi cơ sở (Clarifications 2026-11-12).</summary>
public sealed class Dish
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 1000;

    private Dish() { }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public long Price { get; private set; }
    public string? ImageUrl { get; private set; }
    public DishCategory Category { get; private set; }
    public bool IsFeatured { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public static Dish Create(Guid id, Guid ownerId, DishDetails details, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new DomainRuleException("id", "Mã món không hợp lệ.");
        if (ownerId == Guid.Empty) throw new DomainRuleException("ownerId", "Mã chủ sở hữu không hợp lệ.");
        Dish dish = new() { Id = id, OwnerId = ownerId, CreatedAt = createdAt.ToUniversalTime() };
        dish.Apply(details);
        return dish;
    }

    public void Apply(DishDetails details)
    {
        string name = details.Name?.Trim() ?? "";
        if (name.Length is 0 or > NameMaxLength) throw new DomainRuleException("name", "Tên món dài từ 1 đến 120 ký tự.");
        string? description = string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim();
        if (description is { Length: > DescriptionMaxLength }) throw new DomainRuleException("description", "Mô tả tối đa 1000 ký tự.");
        if (details.Price < 0) throw new DomainRuleException("price", "Giá là số nguyên từ 0 trở lên.");
        if (details.SortOrder < 0) throw new DomainRuleException("sortOrder", "Thứ tự là số nguyên từ 0 trở lên.");
        if (!Enum.IsDefined(details.Category)) throw new DomainRuleException("category", "Nhóm món không hợp lệ.");
        Name = name;
        Description = description;
        Price = details.Price;
        ImageUrl = string.IsNullOrWhiteSpace(details.ImageUrl) ? null : details.ImageUrl.Trim();
        Category = details.Category;
        IsFeatured = details.IsFeatured;
        SortOrder = details.SortOrder;
    }

    public void SoftDelete(DateTimeOffset at) => DeletedAt ??= at.ToUniversalTime();
}

public sealed record DishDetails(string Name, string? Description, long Price, string? ImageUrl, DishCategory Category,
    bool IsFeatured, int SortOrder);

/// <summary>Tình trạng còn món của một món chung tại một cơ sở; thiếu dòng nghĩa là còn món.</summary>
public sealed class BranchDish : IBranchScoped
{
    private BranchDish() { }

    public Guid BranchId { get; private set; }
    public Guid DishId { get; private set; }
    public bool IsAvailable { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static BranchDish Create(Guid branchId, Guid dishId, bool isAvailable, DateTimeOffset at) =>
        new() { BranchId = branchId, DishId = dishId, IsAvailable = isAvailable, UpdatedAt = at.ToUniversalTime() };

    public void SetAvailable(bool isAvailable, DateTimeOffset at)
    {
        IsAvailable = isAvailable;
        UpdatedAt = at.ToUniversalTime();
    }
}
