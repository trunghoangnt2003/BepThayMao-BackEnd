using System.Text.Json.Serialization;
using Bepthaymao.Api.Endpoints.Menu;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Orders;
using Bepthaymao.Application.Orders.ChangeOrderStatus;
using Bepthaymao.Application.Overview;
using Bepthaymao.Domain.Orders;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Endpoints.Orders;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchOptionsRequest([property: JsonRequired] IReadOnlyList<Guid> DishIds, double? DeliveryLat = null, double? DeliveryLng = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderItemRequest([property: JsonRequired] Guid DishId, [property: JsonRequired] int Quantity);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderRequest(
    [property: JsonRequired] string CustomerName,
    [property: JsonRequired] string CustomerPhone,
    [property: JsonRequired] string DeliveryAddress,
    [property: JsonRequired] Guid BranchId,
    [property: JsonRequired] IReadOnlyList<CreateOrderItemRequest> Items,
    double? DeliveryLat = null,
    double? DeliveryLng = null,
    string? Note = null,
    bool NextDayAcknowledged = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelOrderRequest([property: JsonRequired] string Code, string? Phone = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GrantOrderAccessRequest([property: JsonRequired] string Phone);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeOrderStatusRequest([property: JsonRequired] OrderStatus To, CancelReasonCode? ReasonCode = null, string? Reason = null);

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/checkout/branch-options", async (BranchOptionsRequest request,
            [FromServices] IQueryHandler<BranchOptionsQuery, IReadOnlyList<BranchOptionDto>> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(request.DeliveryLat, request.DeliveryLng, request.DishIds ?? []), cancellationToken)))
            .WithName("getBranchOptions").WithTags("Orders").WithSummary("Danh sách cơ sở để khách chọn ở bước xác nhận đơn")
            .WithDescription("Mọi cơ sở đang hoạt động kèm khoảng cách, giờ, cảnh báo giao hôm sau và món đang hết; có tọa độ thì gợi ý cơ sở gần nhất.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests);
        v1.MapPost("/orders", async Task<Results<Created<OrderCreatedDto>, Ok<OrderCreatedDto>>> (CreateOrderRequest request, HttpContext context,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, [FromServices] ICommandHandler<CreateOrderCommand, OrderCreatedResult> handler,
            [FromServices] IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            Guid? parsedKey = null;
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                if (!Guid.TryParse(idempotencyKey, out Guid parsed))
                    throw new ValidationException(new Dictionary<string, string[]> { ["idempotencyKey"] = ["Idempotency-Key phải là UUID."] });
                parsedKey = parsed;
            }
            int windowSeconds = configuration.GetValue("Orders:DuplicateWindowSeconds", 120);
            OrderCreatedResult result = await handler.Handle(new(request.CustomerName, request.CustomerPhone, request.DeliveryAddress, request.BranchId,
                request.DeliveryLat, request.DeliveryLng, request.Note, request.NextDayAcknowledged,
                [.. (request.Items ?? []).Select(item => new CreateOrderItemData(item.DishId, item.Quantity))], parsedKey, windowSeconds), cancellationToken);
            OrderAccessCookie.Append(context.Response, OrderAccessCookie.Read(context.Request), result.Access);
            if (result.Replayed)
            {
                context.Response.Headers["Idempotent-Replayed"] = "true";
                return TypedResults.Ok(result.Created);
            }
            return TypedResults.Created($"/api/v1/orders/{result.Created.Order.Code}", result.Created);
        }).WithName("createOrder").WithTags("Orders").WithSummary("Tạo đơn (khách vãng lai hoặc đã đăng nhập)")
            .WithDescription("Đơn gán cho cơ sở khách chọn; 409 khi ngoài giờ chưa xác nhận, cơ sở không nhận đơn hoặc món đang hết (unavailableDishIds). " +
                "Gửi lặp cùng Idempotency-Key, hoặc trùng số điện thoại + cơ sở + món trong cửa sổ khi đơn trước còn chờ thanh toán: trả 200 kèm header " +
                "Idempotent-Replayed thay vì tạo đơn mới.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status429TooManyRequests);
        v1.MapGet("/orders/{code}", async (string code, [FromQuery] string? phone, HttpContext context,
            [FromServices] IQueryHandler<GetOrderQuery, OrderTrackingDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(code, phone, OrderAccessCookie.Read(context.Request)), cancellationToken)))
            .WithName("getOrder").WithTags("Orders").WithSummary("Theo dõi đơn bằng mã đơn và số điện thoại, hoặc bằng cookie btm_don")
            .WithDescription("Cần phone khớp hoặc cookie btm_don hợp lệ cho đơn này; mọi thất bại trả cùng một 404; payment chỉ có khi còn hạn thanh toán.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status429TooManyRequests);
        v1.MapPost("/orders/{code}/access", async Task<NoContent> (string code, GrantOrderAccessRequest request, HttpContext context,
            [FromServices] ICommandHandler<GrantOrderAccessCommand, IssuedOrderAccess> handler, CancellationToken cancellationToken) =>
        {
            IssuedOrderAccess access = await handler.Handle(new(code, request.Phone), cancellationToken);
            OrderAccessCookie.Append(context.Response, OrderAccessCookie.Read(context.Request), access);
            return TypedResults.NoContent();
        }).WithName("grantOrderAccess").WithTags("Orders").WithSummary("Khách tra đơn bằng mã và số điện thoại, nhận cookie xem đơn")
            .WithDescription("Đổi mã đơn + số điện thoại đúng lấy cookie btm_don để xem/hủy đơn mà không cần đặt số điện thoại vào URL; sai mã hoặc sai số trả 404.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status429TooManyRequests);
        v1.MapPost("/orders/{code}/cancel", async (string code, CancelOrderRequest request, HttpContext context,
            [FromServices] ICommandHandler<CancelOrderCommand, OrderDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(code, request.Phone, OrderAccessCookie.Read(context.Request)), cancellationToken)))
            .WithName("cancelOrder").WithTags("Orders").WithSummary("Khách hủy đơn chưa thanh toán (xác thực bằng mã đơn và số điện thoại, hoặc cookie btm_don)")
            .WithDescription("phone bắt buộc khi không có cookie btm_don hợp lệ; 409 khi đơn đã thanh toán hoặc không còn hủy được.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status429TooManyRequests);

        v1.MapGet("/manager/orders", async ([FromQuery] OrderStatus? status, [FromQuery] PaymentStatus? paymentStatus, [FromQuery] int? page,
            [FromServices] IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(null, status, paymentStatus, page ?? 1), cancellationToken)))
            .WithName("listManagerOrders").WithTags("Manager").WithSummary("Đơn của cơ sở mình (lọc theo trạng thái, thanh toán)")
            .WithDescription("Đơn của cơ sở mình (lọc theo trạng thái, thanh toán)").RequireAuthorization(PolicyNames.ManagerOnly)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        v1.MapPost("/manager/orders/{orderId:guid}/status", async (Guid orderId, ChangeOrderStatusRequest request,
            [FromServices] ICommandHandler<ChangeOrderStatusCommand, OrderDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(orderId, request.To, request.ReasonCode, request.Reason), cancellationToken)))
            .WithName("changeOrderStatus").WithTags("Manager").WithSummary("Chuyển trạng thái đơn (nhận, đang làm, giao, hoàn tất, hủy)")
            .WithDescription("Chỉ chuyển bước kế tiếp Paid→Accepted→Preparing→Delivering→Completed; hủy được từ PendingPayment, Paid, Accepted (D-21); lùi, nhảy bước hoặc lặp trạng thái hiện tại trả 409 (D-12). Đơn ngoài cơ sở trong claim trả 404.")
            .RequireAuthorization(PolicyNames.ManagerOnly)
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        v1.MapGet("/owner/orders", async ([FromQuery] Guid? branchId, [FromQuery] OrderStatus? status, [FromQuery] PaymentStatus? paymentStatus,
            [FromQuery] int? page, [FromServices] IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(branchId, status, paymentStatus, page ?? 1), cancellationToken)))
            .WithName("listOwnerOrders").WithTags("Owner").WithSummary("Đơn của mọi cơ sở mình sở hữu, hoặc một cơ sở")
            .WithDescription("Owner là superadmin trên cơ sở của mình; branchId của Owner khác trả 403.").RequireAuthorization(PolicyNames.OwnerOnly)
            .ProducesOwnerProblems();
        v1.MapPost("/owner/orders/{orderId:guid}/status", async (Guid orderId, ChangeOrderStatusRequest request,
            [FromServices] ICommandHandler<ChangeOrderStatusCommand, OrderDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(orderId, request.To, request.ReasonCode, request.Reason), cancellationToken)))
            .WithName("changeOwnerOrderStatus").WithTags("Owner").WithSummary("Chuyển trạng thái đơn ở mọi cơ sở mình sở hữu (nhận, đang làm, giao, hoàn tất, hủy)")
            .WithDescription("Cùng quy tắc bước với route Manager (D-15a); đơn của Owner khác trả 404.")
            .RequireAuthorization(PolicyNames.OwnerOnly)
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesOwnerProblems()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        v1.MapGet("/owner/overview", async ([FromQuery] Guid? branchId, [FromServices] IQueryHandler<GetOverviewQuery, OverviewDto> handler,
            CancellationToken cancellationToken) => TypedResults.Ok(await handler.Handle(new(branchId), cancellationToken)))
            .WithName("getOwnerOverview").WithTags("Owner").WithSummary("Tổng quan hôm nay của mọi cơ sở mình sở hữu, hoặc một cơ sở")
            .WithDescription("Số đơn, tiền món đã thanh toán, chờ thanh toán, đơn theo giờ, 7 ngày, món đặt nhiều, món đang hết, cơ sở, hoạt động hôm nay (giờ Việt Nam). branchId của Owner khác trả 403.")
            .RequireAuthorization(PolicyNames.OwnerOnly).ProducesOwnerProblems();
        v1.MapGet("/manager/overview", async ([FromServices] IQueryHandler<GetOverviewQuery, OverviewDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(null), cancellationToken)))
            .WithName("getManagerOverview").WithTags("Manager").WithSummary("Tổng quan hôm nay của cơ sở mình")
            .WithDescription("Như tổng quan của Owner, chỉ cơ sở trong claim.").RequireAuthorization(PolicyNames.ManagerOnly)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        return v1;
    }
}
