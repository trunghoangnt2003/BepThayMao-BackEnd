using System.Text.Json.Serialization;
using Bepthaymao.Api.Endpoints.Menu;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Orders;
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
public sealed record CancelOrderRequest([property: JsonRequired] string Code, [property: JsonRequired] string Phone);

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
        v1.MapPost("/orders", async Task<Created<OrderCreatedDto>> (CreateOrderRequest request,
            [FromServices] ICommandHandler<CreateOrderCommand, OrderCreatedDto> handler, CancellationToken cancellationToken) =>
        {
            OrderCreatedDto created = await handler.Handle(new(request.CustomerName, request.CustomerPhone, request.DeliveryAddress, request.BranchId,
                request.DeliveryLat, request.DeliveryLng, request.Note, request.NextDayAcknowledged,
                [.. (request.Items ?? []).Select(item => new CreateOrderItemData(item.DishId, item.Quantity))]), cancellationToken);
            return TypedResults.Created($"/api/v1/orders/{created.Order.Code}", created);
        }).WithName("createOrder").WithTags("Orders").WithSummary("Tạo đơn (khách vãng lai hoặc đã đăng nhập)")
            .WithDescription("Đơn gán cho cơ sở khách chọn; 409 khi ngoài giờ chưa xác nhận, cơ sở không nhận đơn hoặc món đang hết (unavailableDishIds).")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status429TooManyRequests);
        v1.MapGet("/orders/{code}", async (string code, [FromQuery] string phone,
            [FromServices] IQueryHandler<GetOrderQuery, OrderTrackingDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(code, phone), cancellationToken)))
            .WithName("getOrder").WithTags("Orders").WithSummary("Theo dõi đơn bằng mã đơn và số điện thoại")
            .WithDescription("Sai mã, sai số điện thoại hoặc đơn không tồn tại đều trả 404; payment chỉ có khi còn hạn thanh toán.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status429TooManyRequests);
        v1.MapPost("/orders/{code}/cancel", async (string code, CancelOrderRequest request,
            [FromServices] ICommandHandler<CancelOrderCommand, OrderDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(code, request.Phone), cancellationToken)))
            .WithName("cancelOrder").WithTags("Orders").WithSummary("Khách hủy đơn chưa thanh toán (xác thực bằng mã đơn và số điện thoại)")
            .WithDescription("409 khi đơn đã thanh toán hoặc không còn hủy được.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status429TooManyRequests);

        v1.MapGet("/manager/orders", async ([FromQuery] OrderStatus? status, [FromQuery] PaymentStatus? paymentStatus, [FromQuery] int? page,
            [FromServices] IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(null, status, paymentStatus, page ?? 1), cancellationToken)))
            .WithName("listManagerOrders").WithTags("Manager").WithSummary("Đơn của cơ sở mình (lọc theo trạng thái, thanh toán)")
            .WithDescription("Đơn của cơ sở mình (lọc theo trạng thái, thanh toán)").RequireAuthorization(PolicyNames.ManagerOnly)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        v1.MapGet("/owner/orders", async ([FromQuery] Guid? branchId, [FromQuery] OrderStatus? status, [FromQuery] PaymentStatus? paymentStatus,
            [FromQuery] int? page, [FromServices] IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(branchId, status, paymentStatus, page ?? 1), cancellationToken)))
            .WithName("listOwnerOrders").WithTags("Owner").WithSummary("Đơn của mọi cơ sở mình sở hữu, hoặc một cơ sở")
            .WithDescription("Owner là superadmin trên cơ sở của mình; branchId của Owner khác trả 403.").RequireAuthorization(PolicyNames.OwnerOnly)
            .ProducesOwnerProblems();
        return v1;
    }
}
