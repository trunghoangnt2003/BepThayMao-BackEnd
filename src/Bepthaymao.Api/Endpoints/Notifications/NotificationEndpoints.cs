using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Endpoints.Notifications;

public static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/manager/notifications", async (
            [FromQuery] int? page,
            [FromServices] IQueryHandler<ListManagerNotificationsQuery, IReadOnlyList<NotificationDto>> handler,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(page ?? 1), cancellationToken)))
            .WithName("listManagerNotifications").WithTags("Manager")
            .WithSummary("Thông báo in-app của cơ sở mình (mới nhất trước, 20 dòng/trang)")
            .WithDescription("D-23: chỉ trả thông báo kênh InApp thuộc cơ sở trong claim của Manager, không chứa PII.")
            .RequireAuthorization(PolicyNames.ManagerOnly)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        v1.MapGet("/owner/notifications", async (
            [FromQuery] Guid? branchId,
            [FromQuery] int? page,
            [FromServices] IQueryHandler<ListOwnerNotificationsQuery, IReadOnlyList<NotificationDto>> handler,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(branchId, page ?? 1), cancellationToken)))
            .WithName("listOwnerNotifications").WithTags("Owner")
            .WithSummary("Thông báo in-app của mọi cơ sở mình sở hữu (mới nhất trước, 20 dòng/trang)")
            .WithDescription("D-23: trả thông báo của mọi cơ sở Owner sở hữu (hoặc lọc theo branchId), gồm cả cảnh báo TelegramMisconfigured, không chứa PII.")
            .RequireAuthorization(PolicyNames.OwnerOnly)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return v1;
    }
}
