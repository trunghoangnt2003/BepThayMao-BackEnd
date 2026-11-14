using Bepthaymao.Application.Overview;
using Bepthaymao.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Persistence.Menu;

/// <summary>Mọi truy vấn đi qua filter BranchScope: Owner thấy cơ sở của mình, Quản lý thấy cơ sở mình.</summary>
internal sealed class OverviewReader(AppDbContext context) : IOverviewReader
{
    public async Task<IReadOnlyList<OverviewBranch>> BranchesAsync(CancellationToken cancellationToken) =>
        await context.Branches.AsNoTracking().OrderBy(branch => branch.CreatedAt).ThenBy(branch => branch.Id)
            .Select(branch => new OverviewBranch(branch.Id, branch.Name, branch.IsActive, branch.OpenTime, branch.CloseTime,
                branch.BankCode != null && branch.BankCode != "" && branch.BankAccountNo != null && branch.BankAccountNo != ""))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> OrdersSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        await context.Orders.AsNoTracking().Include(order => order.Items)
            .Where(order => order.CreatedAt >= since).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OverviewSoldOut>> SoldOutAsync(CancellationToken cancellationToken) =>
        await context.BranchDishes.AsNoTracking().Where(state => !state.IsAvailable)
            .Join(context.Dishes.Where(dish => dish.DeletedAt == null), state => state.DishId, dish => dish.Id,
                (state, dish) => new OverviewSoldOut(state.BranchId, dish.Name, state.UpdatedAt))
            .ToListAsync(cancellationToken);
}
