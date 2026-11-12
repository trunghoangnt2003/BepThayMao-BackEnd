using Bepthaymao.Domain.Menu;

namespace Bepthaymao.Application.Menu;

/// <summary>Cơ sở đang hoạt động, đọc ngoài filter cho luồng công khai (thực đơn, chọn cơ sở, đặt đơn).</summary>
public sealed record ActiveBranchInfo(Guid Id, Guid OwnerId, string Name, string Address, double Lat, double Lng,
    TimeOnly OpenTime, TimeOnly CloseTime, string? BankCode, string? BankAccountNo, DateTimeOffset CreatedAt)
{
    /// <summary>Đủ thông tin VietQR (mã ngân hàng + số tài khoản) mới nhận đơn (D-08).</summary>
    public bool AcceptsOrders => !string.IsNullOrWhiteSpace(BankCode) && !string.IsNullOrWhiteSpace(BankAccountNo);
}

public interface IMenuStore
{
    /// <summary>Món chưa xóa; <paramref name="ownerId"/> null là mọi Owner (luồng công khai).</summary>
    Task<IReadOnlyList<Dish>> ListDishesAsync(Guid? ownerId, CancellationToken cancellationToken);
    Task<Dish?> FindDishAsync(Guid dishId, CancellationToken cancellationToken);
    void AddDish(Dish dish);
    Task<IReadOnlyList<ActiveBranchInfo>> ListActiveBranchesAsync(CancellationToken cancellationToken);
    /// <summary>Mọi dòng đang hết món, đọc ngoài filter (luồng công khai).</summary>
    Task<IReadOnlyList<BranchDish>> ListUnavailableAsync(CancellationToken cancellationToken);
    /// <summary>Dòng tình trạng của một cơ sở, đi qua filter BranchScope.</summary>
    Task<IReadOnlyList<BranchDish>> ListBranchStatesAsync(Guid branchId, CancellationToken cancellationToken);
    Task<BranchDish?> FindBranchStateAsync(Guid branchId, Guid dishId, CancellationToken cancellationToken);
    void AddBranchState(BranchDish state);
    /// <summary>Tổng số lượng đã đặt ở đơn đã thanh toán, theo món, mọi cơ sở (FR-016).</summary>
    Task<IReadOnlyDictionary<Guid, long>> PaidQuantitiesAsync(CancellationToken cancellationToken);
}

public sealed record MenuDishDto(Guid Id, string Name, string? Description, long Price, string? ImageUrl, DishCategory Category,
    bool IsFeatured, long OrderCount, IReadOnlyList<Guid> UnavailableBranchIds);

public sealed record MenuDishBranchDto(Guid BranchId, string Name, bool IsAvailable, bool IsOpenNow);

public sealed record MenuDishDetailDto(Guid Id, string Name, string? Description, long Price, string? ImageUrl, DishCategory Category,
    bool IsFeatured, long OrderCount, IReadOnlyList<Guid> UnavailableBranchIds, IReadOnlyList<MenuDishBranchDto> Branches);

public sealed record DishDto(Guid Id, string Name, string? Description, long Price, string? ImageUrl, DishCategory Category,
    bool IsFeatured, int SortOrder, long OrderCount, int UnavailableBranchCount);

public sealed record DishInputData(string Name, string? Description, long Price, string? ImageUrl, DishCategory Category,
    bool IsFeatured, int SortOrder)
{
    public DishDetails ToDetails() => new(Name, Description, Price, ImageUrl, Category, IsFeatured, SortOrder);
}

public sealed record BranchDishDto(Guid DishId, Guid BranchId, string Name, long Price, string? ImageUrl, DishCategory Category, bool IsAvailable);

internal static class MenuRules
{
    /// <summary>Món có ở cơ sở khi cùng Owner và không có dòng "hết món".</summary>
    public static bool IsAvailableAt(Dish dish, ActiveBranchInfo branch, ISet<(Guid Branch, Guid Dish)> unavailable) =>
        dish.OwnerId == branch.OwnerId && !unavailable.Contains((branch.Id, dish.Id));

    public static IOrderedEnumerable<Dish> Ordered(IEnumerable<Dish> dishes) =>
        dishes.OrderBy(dish => dish.Category).ThenBy(dish => dish.SortOrder).ThenBy(dish => dish.Name, StringComparer.Ordinal);
}
