using System.Text.Json.Serialization;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches.Owner;
using Bepthaymao.Application.Branches.Owner.CreateOwnerBranch;
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
        group.MapPost("/", async Task<Created<BranchResponse>> (CreateBranchRequest request,
            [FromServices] ICommandHandler<CreateOwnerBranchCommand, BranchResponse> handler, CancellationToken cancellationToken) =>
        {
            BranchResponse response = await handler.Handle(new(request.Branch.ToData(), new(request.Manager.Identifier, request.Manager.Password)), cancellationToken);
            return TypedResults.Created($"/api/v1/owner/branches/{response.Id}", response);
        }).WithName("createOwnerBranch").WithSummary("Tạo cơ sở kèm tài khoản quản lý")
            .WithDescription("Tạo cơ sở kèm tài khoản quản lý trong một transaction; trả 409 khi tên đăng nhập trùng.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
        return v1;
    }
}
