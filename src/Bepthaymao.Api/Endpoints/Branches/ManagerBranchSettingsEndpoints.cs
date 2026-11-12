using System.Text.Json.Serialization;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches;
using Bepthaymao.Application.Branches.Manager;
using Bepthaymao.Application.Branches.Manager.GetManagerBranchSettings;
using Bepthaymao.Application.Branches.Manager.UpdateManagerBranchSettings;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Endpoints.Branches;

/// <summary>Đúng năm trường Quản lý được sửa; thành viên khác (name, isActive, lat, ngân hàng...) bị từ chối bằng 400.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchSettingsRequest(
    [property: JsonRequired] string Phone,
    [property: JsonRequired] string OpenTime,
    [property: JsonRequired] string CloseTime,
    [property: JsonRequired] int AbandonedThresholdMinutes,
    [property: JsonRequired] IReadOnlyList<string> ZaloRecipientIds)
{
    internal BranchSettingsData ToData() => new(Phone, OpenTime, CloseTime, AbandonedThresholdMinutes, ZaloRecipientIds);
}

public static class ManagerBranchSettingsEndpoints
{
    public static RouteGroupBuilder MapManagerBranchSettingsEndpoints(this RouteGroupBuilder v1)
    {
        // Không nhận mã cơ sở từ yêu cầu: cơ sở luôn lấy từ claim branch_id của Quản lý.
        RouteGroupBuilder group = v1.MapGroup("/manager/branch/settings").WithTags("Manager").RequireAuthorization(PolicyNames.ManagerOnly);
        group.MapGet("/", async Task<Ok<ManagerBranchView>> (
            [FromServices] IQueryHandler<GetManagerBranchSettingsQuery, ManagerBranchView> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(), cancellationToken)))
            .WithName("getManagerBranchSettings").WithSummary("Thông số của cơ sở mình")
            .WithDescription("Chỉ vai trò BranchManager; cơ sở lấy từ claim branch_id, không nhận mã cơ sở trong đường dẫn hay thân.")
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/", async Task<Ok<ManagerBranchView>> (BranchSettingsRequest request,
            [FromServices] ICommandHandler<UpdateManagerBranchSettingsCommand, ManagerBranchView> handler, CancellationToken cancellationToken) =>
            TypedResults.Ok(await handler.Handle(new(request.ToData()), cancellationToken)))
            .WithName("updateManagerBranchSettings").WithSummary("Cập nhật thông số cơ sở mình")
            .WithDescription("Cập nhật thông số cơ sở mình; trường chỉ Chủ sở hữu được sửa gửi lên bị 400. Không nhận mã cơ sở; chỉ vai trò BranchManager được gọi.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        return v1;
    }
}
