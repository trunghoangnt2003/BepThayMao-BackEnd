using System.Globalization;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Menu;
using Bepthaymao.Domain.Menu;
using Bepthaymao.Domain.Orders;
using FluentValidation;

namespace Bepthaymao.Application.Orders;

public sealed record BranchOptionsQuery(double? DeliveryLat, double? DeliveryLng, IReadOnlyList<Guid> DishIds) : IQuery<IReadOnlyList<BranchOptionDto>>;

public sealed record BranchOptionDto(Guid BranchId, string Name, string Address, string OpenTime, string CloseTime, double? DistanceKm,
    bool IsOpenNow, bool NextDayWarning, bool IsSuggested, bool AcceptsOrders, IReadOnlyList<Guid> UnavailableDishIds);

public sealed record CreateOrderItemData(Guid DishId, int Quantity);

public sealed record CreateOrderCommand(string CustomerName, string CustomerPhone, string DeliveryAddress, Guid BranchId,
    double? DeliveryLat, double? DeliveryLng, string? Note, bool NextDayAcknowledged, IReadOnlyList<CreateOrderItemData> Items)
    : ICommand<OrderCreatedDto>;

/// <summary>409 có danh sách món đang hết để giao diện nêu tên món (OrderConflictProblem).</summary>
public sealed class DishesUnavailableException(IReadOnlyList<Guid> dishIds)
    : Exception("Có món cơ sở này đang hết. Bỏ món đó hoặc chọn cơ sở khác.")
{
    public IReadOnlyList<Guid> DishIds { get; } = dishIds;
}

internal sealed class BranchOptionsQueryValidator : AbstractValidator<BranchOptionsQuery>
{
    public BranchOptionsQueryValidator()
    {
        RuleFor(query => query.DishIds).NotEmpty().WithMessage("Chọn ít nhất một món.")
            .Must(ids => ids is null || ids.Count <= 100).WithMessage("Tối đa 100 món.");
        RuleFor(query => query.DeliveryLat).InclusiveBetween(-90, 90).When(query => query.DeliveryLat is not null);
        RuleFor(query => query.DeliveryLng).InclusiveBetween(-180, 180).When(query => query.DeliveryLng is not null);
        RuleFor(query => query).Must(query => (query.DeliveryLat is null) == (query.DeliveryLng is null))
            .WithName("deliveryLat").WithMessage("Gửi đủ cả vĩ độ và kinh độ.");
    }
}

internal sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(command => command.CustomerName).NotEmpty().WithMessage("Nhập họ và tên.").MaximumLength(120).WithMessage("Họ và tên tối đa 120 ký tự.");
        RuleFor(command => command.CustomerPhone).Must(phone => PhoneNumbers.Normalize(phone) is not null)
            .WithMessage("Nhập số điện thoại hợp lệ, ví dụ 0901 234 567.");
        RuleFor(command => command.DeliveryAddress).NotEmpty().WithMessage("Nhập địa chỉ giao hàng.").MaximumLength(300).WithMessage("Địa chỉ tối đa 300 ký tự.");
        RuleFor(command => command.Note).MaximumLength(500).WithMessage("Ghi chú tối đa 500 ký tự.");
        RuleFor(command => command.BranchId).NotEmpty().WithMessage("Chọn một cơ sở nhận đơn.");
        RuleFor(command => command.Items).NotEmpty().WithMessage("Chọn ít nhất một món để tiếp tục.");
        RuleForEach(command => command.Items).ChildRules(item =>
            item.RuleFor(value => value.Quantity).InclusiveBetween(1, 99).WithMessage("Số lượng từ 1 đến 99."));
        RuleFor(command => command).Must(command => (command.DeliveryLat is null) == (command.DeliveryLng is null))
            .WithName("deliveryLat").WithMessage("Gửi đủ cả vĩ độ và kinh độ.");
    }
}

internal sealed class BranchOptionsQueryHandler(IMenuStore menu, TimeProvider clock) : IQueryHandler<BranchOptionsQuery, IReadOnlyList<BranchOptionDto>>
{
    public async Task<IReadOnlyList<BranchOptionDto>> Handle(BranchOptionsQuery query, CancellationToken cancellationToken)
    {
        MenuSnapshot snapshot = await MenuSnapshot.LoadAsync(menu, cancellationToken);
        Dictionary<Guid, Dish> dishes = snapshot.Dishes.ToDictionary(dish => dish.Id);
        Guid[] wanted = [.. query.DishIds.Distinct()];
        DateTimeOffset now = clock.GetUtcNow();
        bool located = query.DeliveryLat is not null && query.DeliveryLng is not null;
        List<BranchOptionDto> options = [.. snapshot.Branches.Select(branch =>
        {
            Guid[] missing = [.. wanted.Where(id => !dishes.TryGetValue(id, out Dish? dish) || !MenuRules.IsAvailableAt(dish, branch, snapshot.Unavailable))];
            bool open = OpeningHours.IsOpen(branch.OpenTime, branch.CloseTime, now);
            double? distance = located ? Distance.HaversineKm(query.DeliveryLat!.Value, query.DeliveryLng!.Value, branch.Lat, branch.Lng) : null;
            return new BranchOptionDto(branch.Id, branch.Name, branch.Address, Format(branch.OpenTime), Format(branch.CloseTime), distance,
                open, !open, false, branch.AcceptsOrders, missing);
        })];
        if (!located) return [.. options.OrderBy(option => option.Name, StringComparer.Ordinal)];
        Dictionary<Guid, DateTimeOffset> created = snapshot.Branches.ToDictionary(branch => branch.Id, branch => branch.CreatedAt);
        List<BranchOptionDto> sorted = [.. options.OrderBy(option => option.DistanceKm).ThenBy(option => created[option.BranchId])];
        if (sorted.Count > 0) sorted[0] = sorted[0] with { IsSuggested = true };
        return sorted;
    }

    internal static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

internal sealed class CreateOrderCommandHandler(IMenuStore menu, IOrderStore orders, IPaymentQrService qr, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateOrderCommand, OrderCreatedDto>
{
    public async Task<OrderCreatedDto> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        MenuSnapshot snapshot = await MenuSnapshot.LoadAsync(menu, cancellationToken);
        ActiveBranchInfo branch = snapshot.Branches.FirstOrDefault(value => value.Id == command.BranchId)
            ?? throw new ConflictException("branchId", "Cơ sở này hiện không nhận đơn. Hãy chọn cơ sở khác.");
        if (!branch.AcceptsOrders) throw new ConflictException("branchId", "Cơ sở này tạm chưa nhận đơn. Hãy chọn cơ sở khác.");
        Dictionary<Guid, Dish> dishes = snapshot.Dishes.ToDictionary(dish => dish.Id);
        Guid[] missing = [.. command.Items.Select(item => item.DishId).Distinct()
            .Where(id => !dishes.TryGetValue(id, out Dish? dish) || !MenuRules.IsAvailableAt(dish, branch, snapshot.Unavailable))];
        if (missing.Length > 0) throw new DishesUnavailableException(missing);
        DateTimeOffset now = clock.GetUtcNow();
        bool open = OpeningHours.IsOpen(branch.OpenTime, branch.CloseTime, now);
        if (!open && !command.NextDayAcknowledged)
            throw new ConflictException("nextDayAcknowledged", "Hãy xác nhận để đặt đơn giao vào ngày hôm sau.");
        OrderItem[] items = [.. command.Items.GroupBy(item => item.DishId).Select(group =>
            OrderItem.Create(Guid.CreateVersion7(), group.Key, dishes[group.Key].Name, dishes[group.Key].Price, Math.Min(99, group.Sum(item => item.Quantity))))];
        double? distance = command.DeliveryLat is double lat && command.DeliveryLng is double lng ? Distance.HaversineKm(lat, lng, branch.Lat, branch.Lng) : null;
        string code = await NewCodeAsync(cancellationToken);
        Order order = Order.Place(Guid.CreateVersion7(), code, branch.Id,
            new(command.CustomerName.Trim(), PhoneNumbers.Normalize(command.CustomerPhone)!, command.DeliveryAddress.Trim(),
                command.DeliveryLat, command.DeliveryLng, distance, command.Note),
            items, !open, OpeningHours.DeliveryDate(branch.OpenTime, branch.CloseTime, now), now);
        orders.Add(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        PaymentDto payment = new(qr.CreateQrUrl(branch.BankCode!, branch.BankAccountNo!, order.ItemsTotal, order.Code), order.ItemsTotal, order.Code, order.ExpiresAt);
        return new(order.ToDto(branch.Name, includeCustomer: false), payment, OrderMapping.ShippingNote);
    }

    private async Task<string> NewCodeAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string code = OrderCodes.Next();
            if (!await orders.CodeExistsAsync(code, cancellationToken)) return code;
        }
        throw new InvalidOperationException("Không sinh được mã đơn duy nhất.");
    }
}
