using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches.ListPublicBranches;
using Bepthaymao.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Endpoints.Branches;

public static class PublicBranchesEndpoints
{
    public static RouteGroupBuilder MapPublicBranchesEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/branches", async ([FromServices] IQueryHandler<ListPublicBranchesQuery, IReadOnlyList<PublicBranch>> handler,
            CancellationToken cancellationToken) => TypedResults.Ok(await handler.Handle(new(), cancellationToken)))
            .WithName("listPublicBranches").WithTags("Branches")
            .WithSummary("Danh sách cơ sở đang hoạt động (công khai)")
            .WithDescription("Danh sách cơ sở đang hoạt động; chỉ trả dữ liệu công khai, không yêu cầu đăng nhập.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status429TooManyRequests).ProducesProblem(StatusCodes.Status500InternalServerError);
        return v1;
    }
}
