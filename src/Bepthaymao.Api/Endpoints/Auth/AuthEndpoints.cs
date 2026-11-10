using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Auth.Login;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Endpoints.Auth;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginRequest([property: Required, MaxLength(256)] string Identifier,
    [property: Required, MinLength(10), MaxLength(128)] string Password);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/auth/login", async (LoginRequest request, HttpContext context,
            [FromServices] ICommandHandler<LoginCommand, LoginResult> handler, CancellationToken cancellationToken) =>
        {
            LoginResult result = await handler.Handle(new(request.Identifier, request.Password), cancellationToken);
            RefreshCookie.Append(context.Response, result.Refresh);
            return TypedResults.Ok(result.Tokens);
        }).WithName("login").WithTags("Auth").WithSummary("Đăng nhập nhân sự")
            .WithDescription("Đăng nhập bằng tên đăng nhập và mật khẩu, nhận JWT và cookie refresh HttpOnly.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);
        return v1;
    }
}
