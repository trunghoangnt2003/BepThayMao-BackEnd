using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Branches;
using Bepthaymao.Application.Orders;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Orders;

namespace Bepthaymao.Application.Overview;

/// <summary>Cơ sở trong phạm vi người gọi (filter BranchScope): Owner thấy mọi cơ sở của mình, Quản lý thấy cơ sở mình.</summary>
public sealed record OverviewBranch(Guid Id, string Name, bool IsActive, TimeOnly OpenTime, TimeOnly CloseTime, bool AcceptsOrders);

public sealed record OverviewSoldOut(Guid BranchId, string DishName, DateTimeOffset UpdatedAt);

public interface IOverviewReader
{
    Task<IReadOnlyList<OverviewBranch>> BranchesAsync(CancellationToken cancellationToken);
    /// <summary>Đơn tạo từ <paramref name="since"/> trở đi, kèm món, đi qua filter BranchScope.</summary>
    Task<IReadOnlyList<Order>> OrdersSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
    /// <summary>Món đang hết ở các cơ sở trong phạm vi, kèm tên món.</summary>
    Task<IReadOnlyList<OverviewSoldOut>> SoldOutAsync(CancellationToken cancellationToken);
}

public sealed record OverviewDayDto(DateOnly Date, int Orders, long PaidAmount);

public sealed record OverviewDishDto(string DishName, int Quantity);

public sealed record OverviewSoldOutDto(string DishName, string BranchName);

public sealed record OverviewBranchDto(Guid BranchId, string Name, int OrdersToday, bool IsActive, bool IsOpenNow, bool AcceptsOrders,
    string OpenTime, string CloseTime);

public enum OverviewActivityKind
{
    OrderCreated,
    OrderPaid,
    OrderCancelled,
    DishSoldOut,
}

public sealed record OverviewActivityDto(OverviewActivityKind Kind, DateTimeOffset At, string Title, string Detail);

/// <summary>Số liệu thật từ đơn và tình trạng món; không số liệu bịa (Hallmark Floor 8). Ngày tính theo giờ Việt Nam.</summary>
public sealed record OverviewDto(int OrdersToday, int PaidOrdersToday, long PaidAmountToday, int PendingPayment,
    IReadOnlyList<int> OrdersByHour, int FirstHour, IReadOnlyList<OverviewDayDto> Days, IReadOnlyList<OverviewDishDto> TopDishes,
    IReadOnlyList<OverviewSoldOutDto> SoldOut, IReadOnlyList<OverviewBranchDto> Branches, IReadOnlyList<OverviewActivityDto> Activity);

/// <summary>Owner có thể lọc một cơ sở của mình; Quản lý luôn là cơ sở trong claim (không nhận branchId).</summary>
public sealed record GetOverviewQuery(Guid? BranchId) : IQuery<OverviewDto>;

internal sealed class GetOverviewQueryHandler(ICurrentUser currentUser, IBranchRepository branches, IOverviewReader reader, TimeProvider clock)
    : IQueryHandler<GetOverviewQuery, OverviewDto>
{
    private const int _firstHour = 6;
    private const int _lastHour = 21;
    private const int _days = 7;

    public async Task<OverviewDto> Handle(GetOverviewQuery query, CancellationToken cancellationToken)
    {
        Guid? only = await ResolveBranchAsync(query.BranchId, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(OpeningHours.ToLocal(now).DateTime);
        DateTimeOffset since = now.AddDays(-_days - 1);
        IReadOnlyList<OverviewBranch> scope = [.. (await reader.BranchesAsync(cancellationToken)).Where(branch => only is null || branch.Id == only)];
        HashSet<Guid> ids = [.. scope.Select(branch => branch.Id)];
        Dictionary<Guid, string> names = scope.ToDictionary(branch => branch.Id, branch => branch.Name);
        Order[] orders = [.. (await reader.OrdersSinceAsync(since, cancellationToken)).Where(order => ids.Contains(order.BranchId))];
        Order[] todays = [.. orders.Where(order => LocalDate(order.CreatedAt) == today)];
        OverviewSoldOut[] soldOut = [.. (await reader.SoldOutAsync(cancellationToken)).Where(item => ids.Contains(item.BranchId))];

        return new(
            todays.Length,
            todays.Count(IsPaid),
            todays.Where(IsPaid).Sum(order => order.ItemsTotal),
            todays.Count(order => order.Status == OrderStatus.PendingPayment && order.PaymentStatus == PaymentStatus.Unpaid),
            [.. Enumerable.Range(_firstHour, _lastHour - _firstHour + 1).Select(hour => todays.Count(order => LocalHour(order.CreatedAt) == hour))],
            _firstHour,
            [.. Enumerable.Range(0, _days).Select(offset => today.AddDays(offset - _days + 1)).Select(date => new OverviewDayDto(date,
                orders.Count(order => LocalDate(order.CreatedAt) == date),
                orders.Where(order => LocalDate(order.CreatedAt) == date && IsPaid(order)).Sum(order => order.ItemsTotal)))],
            [.. todays.Where(order => order.Status != OrderStatus.Cancelled).SelectMany(order => order.Items)
                .GroupBy(item => item.DishName).Select(group => new OverviewDishDto(group.Key, group.Sum(item => item.Quantity)))
                .OrderByDescending(dish => dish.Quantity).ThenBy(dish => dish.DishName, StringComparer.Ordinal).Take(5)],
            [.. soldOut.Select(item => new OverviewSoldOutDto(item.DishName, names[item.BranchId]))],
            [.. scope.Select(branch => new OverviewBranchDto(branch.Id, branch.Name, todays.Count(order => order.BranchId == branch.Id),
                branch.IsActive, OpeningHours.IsOpen(branch.OpenTime, branch.CloseTime, now), branch.AcceptsOrders,
                BranchOptionsQueryHandler.Format(branch.OpenTime), BranchOptionsQueryHandler.Format(branch.CloseTime)))],
            Activity(todays, soldOut, names, today));
    }

    private async Task<Guid?> ResolveBranchAsync(Guid? requested, CancellationToken cancellationToken)
    {
        if (currentUser.BranchId is Guid managerBranch) return managerBranch;
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        if (requested is not Guid branchId) return null;
        Branch? branch = await branches.FindAsync(branchId, cancellationToken);
        return branch is not null && branch.OwnerId == ownerId ? branchId : throw new ForbiddenException();
    }

    private static bool IsPaid(Order order) => order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.LatePaid;

    private static DateOnly LocalDate(DateTimeOffset at) => DateOnly.FromDateTime(OpeningHours.ToLocal(at).DateTime);

    private static int LocalHour(DateTimeOffset at) => OpeningHours.ToLocal(at).Hour;

    /// <summary>Dòng thời gian hôm nay từ dữ liệu có sẵn (chưa có nhật ký kiểm toán): đơn mới, đã thanh toán, đã hủy, món hết.</summary>
    private static OverviewActivityDto[] Activity(IEnumerable<Order> todays, IEnumerable<OverviewSoldOut> soldOut,
        Dictionary<Guid, string> names, DateOnly today)
    {
        IEnumerable<OverviewActivityDto> orderEvents = todays.SelectMany(order =>
        {
            string items = string.Join(", ", order.Items.Select(item => $"{item.DishName} × {item.Quantity}"));
            List<OverviewActivityDto> events = [new(OverviewActivityKind.OrderCreated, order.CreatedAt, $"Đơn mới {order.Code}", $"{names[order.BranchId]}, {items}")];
            if (order.CancelledAt is DateTimeOffset cancelled)
                events.Add(new(OverviewActivityKind.OrderCancelled, cancelled, $"Đã hủy {order.Code}", names[order.BranchId]));
            if (IsPaid(order))
                events.Add(new(OverviewActivityKind.OrderPaid, order.CreatedAt, $"Đã thanh toán {order.Code}", $"{order.ItemsTotal:N0} đồng"));
            return events;
        });
        IEnumerable<OverviewActivityDto> stockEvents = soldOut.Where(item => LocalDate(item.UpdatedAt) == today)
            .Select(item => new OverviewActivityDto(OverviewActivityKind.DishSoldOut, item.UpdatedAt, "Hết món", $"{item.DishName} ở {names[item.BranchId]}"));
        return [.. orderEvents.Concat(stockEvents).OrderByDescending(item => item.At).Take(8)];
    }
}
