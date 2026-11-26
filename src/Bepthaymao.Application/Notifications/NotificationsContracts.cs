using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Branches;
using Bepthaymao.Domain.Events;

namespace Bepthaymao.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    NotificationChannel Channel,
    NotificationStatus Status,
    int Attempts,
    Guid BranchId,
    Guid? OrderId,
    string? OrderCode,
    int? WaitedMinutes,
    DateTimeOffset CreatedAt);

public interface INotificationReader
{
    Task<IReadOnlyList<NotificationDto>> ListInAppAsync(Guid? branchId, int page, CancellationToken cancellationToken);
}

public sealed record ListManagerNotificationsQuery(int Page = 1) : IQuery<IReadOnlyList<NotificationDto>>;

internal sealed class ListManagerNotificationsQueryHandler(INotificationReader reader)
    : IQueryHandler<ListManagerNotificationsQuery, IReadOnlyList<NotificationDto>>
{
    public Task<IReadOnlyList<NotificationDto>> Handle(ListManagerNotificationsQuery query, CancellationToken cancellationToken) =>
        reader.ListInAppAsync(null, Math.Max(1, query.Page), cancellationToken);
}

public sealed record ListOwnerNotificationsQuery(Guid? BranchId = null, int Page = 1) : IQuery<IReadOnlyList<NotificationDto>>;

internal sealed class ListOwnerNotificationsQueryHandler(
    INotificationReader reader,
    IBranchRepository branches,
    ICurrentUser currentUser)
    : IQueryHandler<ListOwnerNotificationsQuery, IReadOnlyList<NotificationDto>>
{
    public async Task<IReadOnlyList<NotificationDto>> Handle(ListOwnerNotificationsQuery query, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        if (query.BranchId is Guid branchId)
        {
            Domain.Branches.Branch? branch = await branches.FindAsync(branchId, cancellationToken);
            if (branch is null) throw new NotFoundException();
            if (branch.OwnerId != ownerId) throw new ForbiddenException();
        }
        return await reader.ListInAppAsync(query.BranchId, Math.Max(1, query.Page), cancellationToken);
    }
}
