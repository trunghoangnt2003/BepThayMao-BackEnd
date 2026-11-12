using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Domain.Menu;
using FluentValidation;

namespace Bepthaymao.Application.Menu;

public sealed record ListOwnerDishesQuery : IQuery<IReadOnlyList<DishDto>>;

public sealed record CreateOwnerDishCommand(DishInputData Dish) : ICommand<DishDto>;

public sealed record UpdateOwnerDishCommand(Guid DishId, DishInputData Dish) : ICommand<DishDto>;

public sealed record DeleteOwnerDishCommand(Guid DishId) : ICommand<bool>;

internal sealed class DishInputValidator : AbstractValidator<DishInputData>
{
    public DishInputValidator()
    {
        RuleFor(dish => dish.Name).NotEmpty().WithMessage("Nhập tên món.").MaximumLength(Dish.NameMaxLength).WithMessage("Tên món tối đa 120 ký tự.");
        RuleFor(dish => dish.Description).MaximumLength(Dish.DescriptionMaxLength).WithMessage("Mô tả tối đa 1000 ký tự.");
        RuleFor(dish => dish.Price).GreaterThanOrEqualTo(0).WithMessage("Nhập giá là số nguyên từ 0 trở lên.");
        RuleFor(dish => dish.SortOrder).GreaterThanOrEqualTo(0).WithMessage("Thứ tự là số nguyên từ 0 trở lên.");
        RuleFor(dish => dish.Category).IsInEnum().WithMessage("Nhóm món không hợp lệ.");
        RuleFor(dish => dish.ImageUrl).Must(BeHttpUrl).When(dish => !string.IsNullOrWhiteSpace(dish.ImageUrl))
            .WithMessage("Đường dẫn ảnh phải bắt đầu bằng http:// hoặc https://.");
    }

    private static bool BeHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}

internal sealed class CreateOwnerDishCommandValidator : AbstractValidator<CreateOwnerDishCommand>
{
    public CreateOwnerDishCommandValidator() => RuleFor(command => command.Dish).NotNull().SetValidator(new DishInputValidator());
}

internal sealed class UpdateOwnerDishCommandValidator : AbstractValidator<UpdateOwnerDishCommand>
{
    public UpdateOwnerDishCommandValidator() => RuleFor(command => command.Dish).NotNull().SetValidator(new DishInputValidator());
}

internal sealed class ListOwnerDishesQueryHandler(ICurrentUser currentUser, IMenuStore store)
    : IQueryHandler<ListOwnerDishesQuery, IReadOnlyList<DishDto>>
{
    public async Task<IReadOnlyList<DishDto>> Handle(ListOwnerDishesQuery query, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        IReadOnlyList<Dish> dishes = await store.ListDishesAsync(ownerId, cancellationToken);
        OwnerDishMapper mapper = await OwnerDishMapper.LoadAsync(store, ownerId, cancellationToken);
        return [.. MenuRules.Ordered(dishes).Select(mapper.ToDto)];
    }
}

internal sealed class CreateOwnerDishCommandHandler(ICurrentUser currentUser, IMenuStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateOwnerDishCommand, DishDto>
{
    public async Task<DishDto> Handle(CreateOwnerDishCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        Dish dish = Dish.Create(Guid.CreateVersion7(), ownerId, command.Dish.ToDetails(), clock.GetUtcNow());
        store.AddDish(dish);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await OwnerDishMapper.LoadAsync(store, ownerId, cancellationToken)).ToDto(dish);
    }
}

internal sealed class UpdateOwnerDishCommandHandler(ICurrentUser currentUser, IMenuStore store, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateOwnerDishCommand, DishDto>
{
    public async Task<DishDto> Handle(UpdateOwnerDishCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        Dish dish = await OwnerDishMapper.FindOwnedAsync(store, ownerId, command.DishId, cancellationToken);
        dish.Apply(command.Dish.ToDetails());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await OwnerDishMapper.LoadAsync(store, ownerId, cancellationToken)).ToDto(dish);
    }
}

internal sealed class DeleteOwnerDishCommandHandler(ICurrentUser currentUser, IMenuStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<DeleteOwnerDishCommand, bool>
{
    public async Task<bool> Handle(DeleteOwnerDishCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        Dish dish = await OwnerDishMapper.FindOwnedAsync(store, ownerId, command.DishId, cancellationToken);
        dish.SoftDelete(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal sealed record OwnerDishMapper(IReadOnlyList<ActiveBranchInfo> Branches, ISet<(Guid Branch, Guid Dish)> Unavailable,
    IReadOnlyDictionary<Guid, long> PaidQuantities)
{
    public static async Task<OwnerDishMapper> LoadAsync(IMenuStore store, Guid ownerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ActiveBranchInfo> branches = [.. (await store.ListActiveBranchesAsync(cancellationToken)).Where(branch => branch.OwnerId == ownerId)];
        IReadOnlyList<BranchDish> unavailable = await store.ListUnavailableAsync(cancellationToken);
        return new(branches, unavailable.Select(state => (state.BranchId, state.DishId)).ToHashSet(), await store.PaidQuantitiesAsync(cancellationToken));
    }

    /// <summary>Món của Owner khác hoặc đã xóa trả 404, không lộ sự tồn tại.</summary>
    public static async Task<Dish> FindOwnedAsync(IMenuStore store, Guid ownerId, Guid dishId, CancellationToken cancellationToken)
    {
        Dish? dish = await store.FindDishAsync(dishId, cancellationToken);
        return dish is not null && dish.OwnerId == ownerId ? dish : throw new NotFoundException();
    }

    public DishDto ToDto(Dish dish) => new(dish.Id, dish.Name, dish.Description, dish.Price, dish.ImageUrl, dish.Category, dish.IsFeatured,
        dish.SortOrder, PaidQuantities.GetValueOrDefault(dish.Id), Branches.Count(branch => Unavailable.Contains((branch.Id, dish.Id))));
}
