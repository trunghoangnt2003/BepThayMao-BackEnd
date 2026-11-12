using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Branches;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Menu;

namespace Bepthaymao.Application.Menu;

/// <summary>Quản lý: món chung kèm tình trạng ở cơ sở trong claim. Owner: một cơ sở của mình (FR-003a).</summary>
public sealed record ListBranchDishesQuery(Guid? BranchId) : IQuery<IReadOnlyList<BranchDishDto>>;

public sealed record SetDishAvailabilityCommand(Guid? BranchId, Guid DishId, bool IsAvailable) : ICommand<BranchDishDto>;

internal sealed class ListBranchDishesQueryHandler(ICurrentUser currentUser, IBranchRepository branches, IMenuStore store)
    : IQueryHandler<ListBranchDishesQuery, IReadOnlyList<BranchDishDto>>
{
    public async Task<IReadOnlyList<BranchDishDto>> Handle(ListBranchDishesQuery query, CancellationToken cancellationToken)
    {
        Branch branch = await BranchAccess.ResolveAsync(currentUser, branches, query.BranchId, cancellationToken);
        IReadOnlyList<Dish> dishes = await store.ListDishesAsync(branch.OwnerId, cancellationToken);
        Dictionary<Guid, bool> states = (await store.ListBranchStatesAsync(branch.Id, cancellationToken))
            .ToDictionary(state => state.DishId, state => state.IsAvailable);
        return [.. MenuRules.Ordered(dishes).Select(dish => ToDto(branch.Id, dish, states.GetValueOrDefault(dish.Id, true)))];
    }

    internal static BranchDishDto ToDto(Guid branchId, Dish dish, bool isAvailable) =>
        new(dish.Id, branchId, dish.Name, dish.Price, dish.ImageUrl, dish.Category, isAvailable);
}

internal sealed class SetDishAvailabilityCommandHandler(ICurrentUser currentUser, IBranchRepository branches, IMenuStore store,
    IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<SetDishAvailabilityCommand, BranchDishDto>
{
    public async Task<BranchDishDto> Handle(SetDishAvailabilityCommand command, CancellationToken cancellationToken)
    {
        Branch branch = await BranchAccess.ResolveAsync(currentUser, branches, command.BranchId, cancellationToken);
        Dish? dish = await store.FindDishAsync(command.DishId, cancellationToken);
        if (dish is null || dish.OwnerId != branch.OwnerId) throw new NotFoundException();
        DateTimeOffset now = clock.GetUtcNow();
        BranchDish? state = await store.FindBranchStateAsync(branch.Id, dish.Id, cancellationToken);
        if (state is null) store.AddBranchState(BranchDish.Create(branch.Id, dish.Id, command.IsAvailable, now));
        else state.SetAvailable(command.IsAvailable, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ListBranchDishesQueryHandler.ToDto(branch.Id, dish, command.IsAvailable);
    }
}

internal static class BranchAccess
{
    /// <summary>
    /// Quản lý luôn dùng cơ sở trong claim (không nhận branchId). Owner phải chỉ định cơ sở và cơ sở phải thuộc mình:
    /// filter BranchScope trả null cho cơ sở của Owner khác nên kết quả là 404, khớp D-13.
    /// </summary>
    public static async Task<Branch> ResolveAsync(ICurrentUser user, IBranchRepository branches, Guid? requestedBranchId,
        CancellationToken cancellationToken)
    {
        if (user.BranchId is Guid managerBranch && requestedBranchId is null)
            return await branches.FindAsync(managerBranch, cancellationToken) ?? throw new NotFoundException();
        Guid ownerId = user.OwnerId ?? throw new ForbiddenException();
        Guid branchId = requestedBranchId ?? throw new ForbiddenException();
        Branch branch = await branches.FindAsync(branchId, cancellationToken) ?? throw new NotFoundException();
        return branch.OwnerId == ownerId ? branch : throw new ForbiddenException();
    }
}
