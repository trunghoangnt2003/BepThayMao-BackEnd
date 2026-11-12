using Bepthaymao.Application.Menu;
using Bepthaymao.Application.Orders;
using Bepthaymao.Application.Shop;
using Bepthaymao.Domain.Menu;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Domain.Shop;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Persistence.Menu;

internal sealed class MenuStore(AppDbContext context) : IMenuStore
{
    public async Task<IReadOnlyList<Dish>> ListDishesAsync(Guid? ownerId, CancellationToken cancellationToken) =>
        await context.Dishes.Where(dish => dish.DeletedAt == null && (ownerId == null || dish.OwnerId == ownerId)).ToListAsync(cancellationToken);

    public Task<Dish?> FindDishAsync(Guid dishId, CancellationToken cancellationToken) =>
        context.Dishes.FirstOrDefaultAsync(dish => dish.Id == dishId && dish.DeletedAt == null, cancellationToken);

    public void AddDish(Dish dish) => context.Dishes.Add(dish);

    public async Task<IReadOnlyList<ActiveBranchInfo>> ListActiveBranchesAsync(CancellationToken cancellationToken) =>
        await context.Branches.AsNoTracking().IgnoreQueryFilters([AppDbContext.BranchScope]).Where(branch => branch.IsActive)
            .OrderBy(branch => branch.CreatedAt).ThenBy(branch => branch.Id)
            .Select(branch => new ActiveBranchInfo(branch.Id, branch.OwnerId, branch.Name, branch.Address, branch.Lat, branch.Lng,
                branch.OpenTime, branch.CloseTime, branch.BankCode, branch.BankAccountNo, branch.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BranchDish>> ListUnavailableAsync(CancellationToken cancellationToken) =>
        await context.BranchDishes.AsNoTracking().IgnoreQueryFilters([AppDbContext.BranchScope]).Where(state => !state.IsAvailable)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BranchDish>> ListBranchStatesAsync(Guid branchId, CancellationToken cancellationToken) =>
        await context.BranchDishes.Where(state => state.BranchId == branchId).ToListAsync(cancellationToken);

    public Task<BranchDish?> FindBranchStateAsync(Guid branchId, Guid dishId, CancellationToken cancellationToken) =>
        context.BranchDishes.FirstOrDefaultAsync(state => state.BranchId == branchId && state.DishId == dishId, cancellationToken);

    public void AddBranchState(BranchDish state) => context.BranchDishes.Add(state);

    public async Task<IReadOnlyDictionary<Guid, long>> PaidQuantitiesAsync(CancellationToken cancellationToken) =>
        await context.Orders.AsNoTracking().IgnoreQueryFilters([AppDbContext.BranchScope])
            .Where(order => order.PaymentStatus == PaymentStatus.Paid)
            .SelectMany(order => order.Items)
            .GroupBy(item => item.DishId)
            .Select(group => new { group.Key, Quantity = group.Sum(item => (long)item.Quantity) })
            .ToDictionaryAsync(row => row.Key, row => row.Quantity, cancellationToken);
}

internal sealed class ShopStore(AppDbContext context) : IShopStore
{
    public async Task<IReadOnlyList<ShopBanner>> ListBannersAsync(Guid? ownerId, CancellationToken cancellationToken) =>
        await context.ShopBanners.Where(banner => ownerId == null || banner.OwnerId == ownerId).ToListAsync(cancellationToken);

    public Task<ShopBanner?> FindBannerAsync(Guid bannerId, CancellationToken cancellationToken) =>
        context.ShopBanners.FirstOrDefaultAsync(banner => banner.Id == bannerId, cancellationToken);

    public void AddBanner(ShopBanner banner) => context.ShopBanners.Add(banner);

    public void RemoveBanner(ShopBanner banner) => context.ShopBanners.Remove(banner);

    public Task<ShopAbout?> FindAboutAsync(Guid? ownerId, CancellationToken cancellationToken) =>
        context.ShopAbouts.Where(about => ownerId == null || about.OwnerId == ownerId)
            .OrderByDescending(about => about.UpdatedAt).FirstOrDefaultAsync(cancellationToken);

    public void AddAbout(ShopAbout about) => context.ShopAbouts.Add(about);
}

internal sealed class OrderStore(AppDbContext context) : IOrderStore
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        context.Orders.IgnoreQueryFilters([AppDbContext.BranchScope]).AnyAsync(order => order.Code == code, cancellationToken);

    public void Add(Order order) => context.Orders.Add(order);

    public Task<Order?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Orders.IgnoreQueryFilters([AppDbContext.BranchScope]).Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListAsync(Guid? branchId, OrderStatus? status, PaymentStatus? paymentStatus, int page, int pageSize,
        CancellationToken cancellationToken) =>
        await context.Orders.AsNoTracking().Include(order => order.Items)
            .Where(order => (branchId == null || order.BranchId == branchId) && (status == null || order.Status == status)
                && (paymentStatus == null || order.PaymentStatus == paymentStatus))
            .OrderByDescending(order => order.CreatedAt).ThenBy(order => order.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, OrderBranchInfo>> BranchesAsync(IEnumerable<Guid> branchIds, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. branchIds];
        return await context.Branches.AsNoTracking().IgnoreQueryFilters([AppDbContext.BranchScope]).Where(branch => ids.Contains(branch.Id))
            .Select(branch => new OrderBranchInfo(branch.Id, branch.Name, branch.BankCode, branch.BankAccountNo))
            .ToDictionaryAsync(branch => branch.Id, cancellationToken);
    }
}
