using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Security;

internal sealed class SameOriginFilter(IConfiguration configuration, IProblemDetailsService problems) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.Headers.TryGetValue("Origin", out var origins)
            && (origins.Count != 1 || !(configuration.GetSection("Auth:AllowedOrigins").Get<string[]>() ?? [])
                .Contains(origins[0], StringComparer.Ordinal)))
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Forbidden origin." },
            });
            return Results.Empty;
        }
        return await next(context);
    }
}
