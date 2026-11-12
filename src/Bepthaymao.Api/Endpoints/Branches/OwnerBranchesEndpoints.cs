using System.Text.Json.Serialization;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches.Owner;
using Bepthaymao.Application.Branches.Owner.CreateOwnerBranch;
using Bepthaymao.Application.Branches.Owner.GetOwnerBranch;
using Bepthaymao.Application.Branches.Owner.ListOwnerBranches;
using Bepthaymao.Application.Branches.Owner.UpdateOwnerBranch;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Endpoints.Branches;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchOwnerSettingsRequest(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Address,
    [property: JsonRequired] string Phone,
    [property: JsonRequired] double Lat,
    [property: JsonRequired] double Lng,
    [property: JsonRequired] string OpenTime,
    [property: JsonRequired] string CloseTime,
    [property: JsonRequired] int AbandonedThresholdMinutes,
    [property: JsonRequired] IReadOnlyList<string> ZaloRecipientIds,
    [property: JsonRequired] bool IsActive,
    string? BankAccountNo = null,
    string? BankCode = null,
    string? SepayAccountRef = null)
{
    internal BranchOwnerSettingsData ToData() => new(Name, Address, Phone, Lat, Lng, OpenTime, CloseTime,
        AbandonedThresholdMinutes, ZaloRecipientIds, BankAccountNo, BankCode, SepayAccountRef, IsActive);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagerAccountRequest([property: JsonRequired] string Identifier, [property: JsonRequired] string Password);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateBranchRequest([property: JsonRequired] BranchOwnerSettingsRequest Branch,
    [property: JsonRequired] ManagerAccountRequest Manager);

public static class OwnerBranchesEndpoints
{
    public static RouteGroupBuilder MapOwnerBranchesEndpoints(this RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/owner/branches").WithTags("Owner").RequireAuthorization(PolicyNames.OwnerOnly);
        group.MapGet("/", async Task<Ok<IReadOnlyList<BranchResponse>>> (
            [FromServices] IQueryHandler<ListOwnerBranchesQuery, IReadOnlyList<BranchResponse>> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(), cancellationToken)))
            .WithName("listOwnerBranches").WithSummary("Danh sách cơ sở của chủ sở hữu")
            .WithDescription("Chỉ trả cơ sở của Chủ sở hữu đang đăng nhập; Quản lý gọi /owner/* nhận 403.")
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapGet("/{branchId:guid}", async Task<Ok<BranchResponse>> (Guid branchId,
            [FromServices] IQueryHandler<GetOwnerBranchQuery, BranchResponse> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(branchId), cancellationToken)))
            .WithName("getOwnerBranch").WithSummary("Xem thông tin một cơ sở của chủ sở hữu")
            .WithDescription("Trả 404 khi cơ sở không tồn tại hoặc thuộc Chủ sở hữu khác.")
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/{branchId:guid}", async Task<Ok<BranchResponse>> (Guid branchId, BranchOwnerSettingsRequest request,
            [FromServices] ICommandHandler<UpdateOwnerBranchCommand, BranchResponse> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(branchId, request.ToData()), cancellationToken)))
            .WithName("updateOwnerBranch").WithSummary("Sửa thông tin cơ sở, bật hoặc tắt nhận đơn (isActive)")
            .WithDescription("Chủ sở hữu sửa mọi trường và bật hoặc tắt nhận đơn; tắt cơ sở không xóa cơ sở hay tài khoản quản lý. Trả 404 khi cơ sở thuộc Chủ sở hữu khác.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", async Task<Created<BranchResponse>> (CreateBranchRequest request,
            [FromServices] ICommandHandler<CreateOwnerBranchCommand, BranchResponse> handler, CancellationToken cancellationToken) =>
        {
            if (request.Branch is null || request.Manager is null)
                throw new ValidationException(new Dictionary<string, string[]> { [request.Branch is null ? "branch" : "manager"] = ["Trường này là bắt buộc."] });
            BranchResponse response = await handler.Handle(new(request.Branch.ToData(), new(request.Manager.Identifier, request.Manager.Password)), cancellationToken);
            return TypedResults.Created($"/api/v1/owner/branches/{response.Id}", response);
        }).WithName("createOwnerBranch").WithSummary("Tạo cơ sở kèm tài khoản quản lý")
            .WithDescription("Tạo cơ sở kèm tài khoản quản lý trong một transaction; trả 409 khi tên đăng nhập trùng.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
        return v1;
    }
}
