using System.Text.Json.Serialization;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Menu;
using Bepthaymao.Application.Shop;
using Bepthaymao.Domain.Menu;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Endpoints.Menu;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DishInputRequest([property: JsonRequired] string Name, [property: JsonRequired] long Price,
    [property: JsonRequired] DishCategory Category, string? Description = null, string? ImageUrl = null, bool IsFeatured = false, int SortOrder = 0)
{
    internal DishInputData ToData() => new(Name, Description, Price, ImageUrl, Category, IsFeatured, SortOrder);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DishAvailabilityRequest([property: JsonRequired] bool IsAvailable);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BannerInputRequest([property: JsonRequired] string Title, [property: JsonRequired] int SortOrder,
    [property: JsonRequired] bool IsActive, string? Body = null, string? ImageUrl = null, Guid? LinkDishId = null)
{
    internal BannerInputData ToData() => new(Title, Body, ImageUrl, LinkDishId, SortOrder, IsActive);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AboutInputRequest([property: JsonRequired] string About);

public static class MenuEndpoints
{
    public static RouteGroupBuilder MapMenuEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/menu", async ([FromServices] IQueryHandler<GetMenuQuery, IReadOnlyList<MenuDishDto>> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(), cancellationToken)))
            .WithName("getMenu").WithTags("Menu").WithSummary("Thực đơn chung của quán, kèm cơ sở nào đang hết từng món")
            .WithDescription("Thực đơn chung; xem được trước khi chọn cơ sở, món xóa mềm không trả về.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public).ProducesProblem(StatusCodes.Status429TooManyRequests);
        v1.MapGet("/menu/{dishId:guid}", async (Guid dishId, [FromServices] IQueryHandler<GetMenuDishQuery, MenuDishDetailDto> handler,
            CancellationToken cancellationToken) => TypedResults.Ok(await handler.Handle(new(dishId), cancellationToken)))
            .WithName("getMenuDish").WithTags("Menu").WithSummary("Chi tiết món và tình trạng còn món ở từng cơ sở đang hoạt động")
            .WithDescription("Chi tiết món; 404 nếu món không tồn tại hoặc đã xóa mềm.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status429TooManyRequests);
        v1.MapGet("/shop/content", async ([FromServices] IQueryHandler<GetShopContentQuery, ShopContentDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(), cancellationToken)))
            .WithName("getShopContent").WithTags("Shop").WithSummary("Banner đang bật và đoạn giới thiệu quán cho trang chủ khách")
            .WithDescription("Banner theo sortOrder tăng dần, chỉ banner isActive; about null nếu chủ quán chưa nhập.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public).ProducesProblem(StatusCodes.Status429TooManyRequests);

        RouteGroupBuilder owner = v1.MapGroup("/owner").WithTags("Owner").RequireAuthorization(PolicyNames.OwnerOnly);
        owner.MapGet("/dishes", async ([FromServices] IQueryHandler<ListOwnerDishesQuery, IReadOnlyList<DishDto>> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(), cancellationToken)))
            .WithName("listOwnerDishes").WithSummary("Thực đơn chung (gồm số cơ sở đang hết từng món)")
            .WithDescription("Thực đơn chung của Chủ sở hữu đang đăng nhập.").ProducesOwnerProblems();
        owner.MapPost("/dishes", async Task<Created<DishDto>> (DishInputRequest request,
            [FromServices] ICommandHandler<CreateOwnerDishCommand, DishDto> handler, CancellationToken cancellationToken) =>
        {
            DishDto dish = await handler.Handle(new(request.ToData()), cancellationToken);
            return TypedResults.Created($"/api/v1/owner/dishes/{dish.Id}", dish);
        }).WithName("createOwnerDish").WithSummary("Thêm món vào thực đơn chung").WithDescription("Món mới mặc định còn ở mọi cơ sở.")
            .ProducesOwnerProblems().ProducesProblem(StatusCodes.Status400BadRequest);
        owner.MapPut("/dishes/{dishId:guid}", async (Guid dishId, DishInputRequest request,
            [FromServices] ICommandHandler<UpdateOwnerDishCommand, DishDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(dishId, request.ToData()), cancellationToken)))
            .WithName("updateOwnerDish").WithSummary("Sửa món (giá áp dụng mọi cơ sở)").WithDescription("Đơn đã đặt giữ tên và giá cũ.")
            .ProducesOwnerProblems().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        owner.MapDelete("/dishes/{dishId:guid}", async Task<NoContent> (Guid dishId,
            [FromServices] ICommandHandler<DeleteOwnerDishCommand, bool> handler, CancellationToken cancellationToken) =>
        {
            await handler.Handle(new(dishId), cancellationToken);
            return TypedResults.NoContent();
        }).WithName("deleteOwnerDish").WithSummary("Xóa món (xóa mềm)").WithDescription("Xóa món (xóa mềm)")
            .ProducesOwnerProblems().ProducesProblem(StatusCodes.Status404NotFound);
        owner.MapGet("/branches/{branchId:guid}/dishes", async (Guid branchId,
            [FromServices] IQueryHandler<ListBranchDishesQuery, IReadOnlyList<BranchDishDto>> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(branchId), cancellationToken)))
            .WithName("listOwnerBranchDishes").WithSummary("Tình trạng còn món ở một cơ sở của mình")
            .WithDescription("Tình trạng còn món ở một cơ sở của mình").ProducesOwnerProblems().ProducesProblem(StatusCodes.Status404NotFound);
        owner.MapPut("/branches/{branchId:guid}/dishes/{dishId:guid}/availability", async (Guid branchId, Guid dishId, DishAvailabilityRequest request,
            [FromServices] ICommandHandler<SetDishAvailabilityCommand, BranchDishDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(branchId, dishId, request.IsAvailable), cancellationToken)))
            .WithName("setOwnerDishAvailability").WithSummary("Bật/tắt còn món ở một cơ sở của mình")
            .WithDescription("Bật/tắt còn món ở một cơ sở của mình").ProducesOwnerProblems().ProducesProblem(StatusCodes.Status404NotFound);
        owner.MapGet("/shop-content/banners", async ([FromServices] IQueryHandler<ListOwnerBannersQuery, IReadOnlyList<BannerDto>> handler,
            CancellationToken cancellationToken) => TypedResults.Ok(await handler.Handle(new(), cancellationToken)))
            .WithName("listOwnerBanners").WithSummary("Mọi banner (kể cả đang ẩn)").WithDescription("Mọi banner (kể cả đang ẩn)").ProducesOwnerProblems();
        owner.MapPost("/shop-content/banners", async Task<Created<BannerDto>> (BannerInputRequest request,
            [FromServices] ICommandHandler<CreateOwnerBannerCommand, BannerDto> handler, CancellationToken cancellationToken) =>
        {
            BannerDto banner = await handler.Handle(new(request.ToData()), cancellationToken);
            return TypedResults.Created($"/api/v1/owner/shop-content/banners/{banner.Id}", banner);
        }).WithName("createOwnerBanner").WithSummary("Thêm banner").WithDescription("Thêm banner")
            .ProducesOwnerProblems().ProducesProblem(StatusCodes.Status400BadRequest);
        owner.MapPut("/shop-content/banners/{bannerId:guid}", async (Guid bannerId, BannerInputRequest request,
            [FromServices] ICommandHandler<UpdateOwnerBannerCommand, BannerDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(bannerId, request.ToData()), cancellationToken)))
            .WithName("updateOwnerBanner").WithSummary("Sửa, bật/tắt hoặc đổi thứ tự banner").WithDescription("Sửa, bật/tắt hoặc đổi thứ tự banner")
            .ProducesOwnerProblems().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        owner.MapDelete("/shop-content/banners/{bannerId:guid}", async Task<NoContent> (Guid bannerId,
            [FromServices] ICommandHandler<DeleteOwnerBannerCommand, bool> handler, CancellationToken cancellationToken) =>
        {
            await handler.Handle(new(bannerId), cancellationToken);
            return TypedResults.NoContent();
        }).WithName("deleteOwnerBanner").WithSummary("Xóa banner").WithDescription("Xóa banner")
            .ProducesOwnerProblems().ProducesProblem(StatusCodes.Status404NotFound);
        owner.MapPut("/shop-content/about", async (AboutInputRequest request,
            [FromServices] ICommandHandler<UpdateOwnerAboutCommand, ShopContentDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(request.About), cancellationToken)))
            .WithName("updateOwnerAbout").WithSummary("Sửa đoạn giới thiệu quán").WithDescription("Chuỗi rỗng xóa đoạn giới thiệu.")
            .ProducesOwnerProblems().ProducesProblem(StatusCodes.Status400BadRequest);

        RouteGroupBuilder manager = v1.MapGroup("/manager").WithTags("Manager").RequireAuthorization(PolicyNames.ManagerOnly);
        manager.MapGet("/dishes", async ([FromServices] IQueryHandler<ListBranchDishesQuery, IReadOnlyList<BranchDishDto>> handler,
            CancellationToken cancellationToken) => TypedResults.Ok(await handler.Handle(new(null), cancellationToken)))
            .WithName("listManagerDishes").WithSummary("Món của thực đơn chung kèm tình trạng còn món ở cơ sở mình")
            .WithDescription("Quản lý không thêm, sửa, xóa món; chỉ bật/tắt còn món.")
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        manager.MapPut("/dishes/{dishId:guid}/availability", async (Guid dishId, DishAvailabilityRequest request,
            [FromServices] ICommandHandler<SetDishAvailabilityCommand, BranchDishDto> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(null, dishId, request.IsAvailable), cancellationToken)))
            .WithName("setManagerDishAvailability").WithSummary("Bật/tắt còn món ở cơ sở mình").WithDescription("Bật/tắt còn món ở cơ sở mình")
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        return v1;
    }

    internal static RouteHandlerBuilder ProducesOwnerProblems(this RouteHandlerBuilder builder) =>
        builder.ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
}
