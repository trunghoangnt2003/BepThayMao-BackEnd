using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Orders;
using Bepthaymao.Domain.Menu;

namespace Bepthaymao.Application.Menu;

public sealed record GetMenuQuery : IQuery<IReadOnlyList<MenuDishDto>>;

public sealed record GetMenuDishQuery(Guid DishId) : IQuery<MenuDishDetailDto>;

internal sealed class GetMenuQueryHandler(IMenuStore store) : IQueryHandler<GetMenuQuery, IReadOnlyList<MenuDishDto>>
{
    public async Task<IReadOnlyList<MenuDishDto>> Handle(GetMenuQuery query, CancellationToken cancellationToken)
    {
        MenuSnapshot snapshot = await MenuSnapshot.LoadAsync(store, cancellationToken);
        return [.. MenuRules.Ordered(snapshot.Dishes).Select(snapshot.ToDto)];
    }
}

internal sealed class GetMenuDishQueryHandler(IMenuStore store, TimeProvider clock) : IQueryHandler<GetMenuDishQuery, MenuDishDetailDto>
{
    public async Task<MenuDishDetailDto> Handle(GetMenuDishQuery query, CancellationToken cancellationToken)
    {
        MenuSnapshot snapshot = await MenuSnapshot.LoadAsync(store, cancellationToken);
        Dish dish = snapshot.Dishes.FirstOrDefault(value => value.Id == query.DishId) ?? throw new NotFoundException();
        MenuDishDto dto = snapshot.ToDto(dish);
        DateTimeOffset now = clock.GetUtcNow();
        MenuDishBranchDto[] branches = [.. snapshot.Branches.Where(branch => branch.OwnerId == dish.OwnerId)
            .OrderBy(branch => branch.Name, StringComparer.Ordinal)
            .Select(branch => new MenuDishBranchDto(branch.Id, branch.Name,
                MenuRules.IsAvailableAt(dish, branch, snapshot.Unavailable), OpeningHours.IsOpen(branch.OpenTime, branch.CloseTime, now)))];
        return new(dto.Id, dto.Name, dto.Description, dto.Price, dto.ImageUrl, dto.Category, dto.IsFeatured, dto.OrderCount,
            dto.UnavailableBranchIds, branches);
    }
}

internal sealed record MenuSnapshot(IReadOnlyList<Dish> Dishes, IReadOnlyList<ActiveBranchInfo> Branches,
    ISet<(Guid Branch, Guid Dish)> Unavailable, IReadOnlyDictionary<Guid, long> PaidQuantities)
{
    public static async Task<MenuSnapshot> LoadAsync(IMenuStore store, CancellationToken cancellationToken)
    {
        IReadOnlyList<Dish> dishes = await store.ListDishesAsync(null, cancellationToken);
        IReadOnlyList<ActiveBranchInfo> branches = await store.ListActiveBranchesAsync(cancellationToken);
        IReadOnlyList<BranchDish> unavailable = await store.ListUnavailableAsync(cancellationToken);
        IReadOnlyDictionary<Guid, long> quantities = await store.PaidQuantitiesAsync(cancellationToken);
        return new(dishes, branches, unavailable.Select(state => (state.BranchId, state.DishId)).ToHashSet(), quantities);
    }

    public MenuDishDto ToDto(Dish dish) => new(dish.Id, dish.Name, dish.Description, dish.Price, dish.ImageUrl, dish.Category,
        dish.IsFeatured, PaidQuantities.GetValueOrDefault(dish.Id),
        [.. Branches.Where(branch => !MenuRules.IsAvailableAt(dish, branch, Unavailable)).Select(branch => branch.Id)]);
}
