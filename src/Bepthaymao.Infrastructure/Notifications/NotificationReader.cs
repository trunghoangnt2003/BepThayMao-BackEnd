using Bepthaymao.Application.Notifications;
using Bepthaymao.Domain.Events;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Notifications;

internal sealed class NotificationReader(AppDbContext db) : INotificationReader
{
    public const int PageSize = 20;

    public async Task<IReadOnlyList<NotificationDto>> ListInAppAsync(Guid? branchId, int page, CancellationToken cancellationToken)
    {
        IQueryable<Notification> query = db.Notifications
            .AsNoTracking()
            .Where(n => n.Channel == NotificationChannel.InApp);

        if (branchId is Guid id)
            query = query.Where(n => n.BranchId == id);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(n => new NotificationDto(
                n.Id,
                n.Type,
                n.Channel,
                n.Status,
                n.Attempts,
                n.BranchId,
                n.OrderId,
                n.OrderCode,
                n.WaitedMinutes,
                n.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
