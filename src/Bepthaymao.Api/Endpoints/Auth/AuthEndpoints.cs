using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Auth.Login;
using Bepthaymao.Application.Auth.Refresh;
using Bepthaymao.Application.Auth.Logout;
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
            [FromServices] ICommandHandler<LoginCommand, LoginResult> handler,
            [FromServices] LoginAttemptLimiter attempts, CancellationToken cancellationToken) =>
        {
            attempts.EnsureAllowed(context.Connection.RemoteIpAddress?.ToString(), request.Identifier);
            LoginResult result = await handler.Handle(new(request.Identifier, request.Password), cancellationToken);
            RefreshCookie.Append(context.Response, result.Refresh);
            return TypedResults.Ok(result.Tokens);
        }).WithName("login").WithTags("Auth").WithSummary("Đăng nhập nhân sự")
            .WithDescription("Đăng nhập bằng tên đăng nhập và mật khẩu, nhận JWT và cookie refresh HttpOnly.")
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Login)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);
        v1.MapPost("/auth/refresh", async (HttpContext context,
            [FromServices] ICommandHandler<RefreshSessionCommand, LoginResult> handler, CancellationToken cancellationToken) =>
        {
            LoginResult result = await handler.Handle(new(context.Request.Cookies[RefreshCookie.Name]), cancellationToken);
            RefreshCookie.Append(context.Response, result.Refresh);
            return TypedResults.Ok(result.Tokens);
        }).WithName("refreshToken").WithTags("Auth").WithSummary("Làm mới phiên đăng nhập")
            .WithDescription("Xoay vòng refresh cookie và phát JWT với quyền hiện tại.")
            .AddEndpointFilter<SameOriginFilter>().AllowAnonymous().ProducesProblem(401).ProducesProblem(403);
        v1.MapPost("/auth/logout", async (HttpContext context,
            [FromServices] ICommandHandler<LogoutCommand, bool> handler, CancellationToken cancellationToken) =>
        {
            await handler.Handle(new(context.Request.Cookies[RefreshCookie.Name]), cancellationToken);
            RefreshCookie.Delete(context.Response);
            return TypedResults.NoContent();
        }).WithName("logout").WithTags("Auth").WithSummary("Đăng xuất mọi phiên")
            .WithDescription("Thu hồi mọi refresh token của tài khoản và xóa cookie; không có cookie vẫn trả 204.")
            .AddEndpointFilter<SameOriginFilter>().AllowAnonymous().ProducesProblem(403);
        return v1;
    }
}
