using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches.ListPublicBranches;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Endpoints.Branches;

internal static class PublicBranchesEndpoints
{
    public static RouteGroupBuilder MapPublicBranchesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/branches", async (
                IQueryHandler<ListPublicBranchesQuery, IReadOnlyList<PublicBranch>> handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.Handle(new ListPublicBranchesQuery(), cancellationToken).ConfigureAwait(false)))
            .WithName("listPublicBranches")
            .WithTags("Branches")
            .WithSummary("Danh sách cơ sở đang hoạt động")
            .WithDescription("Chỉ trả thông tin công khai tối thiểu; không lộ tọa độ, cấu hình ngân hàng, SePay, Zalo hay cờ hoạt động.")
            .AllowAnonymous()
            .Produces<IReadOnlyList<PublicBranch>>()
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")
            .RequireRateLimiting(RateLimitPolicies.Public);
        return group;
    }
}
