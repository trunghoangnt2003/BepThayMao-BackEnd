using System.Threading.RateLimiting;
using System.Globalization;

namespace Bepthaymao.Api.Security;

public static class RateLimitPolicies
{
    public const string Public = "public";
}

internal static class RateLimitingSetup
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        int permitLimit = configuration.GetValue("RateLimiting:Public:PermitLimit", 120);
        int windowSeconds = configuration.GetValue("RateLimiting:Public:WindowSeconds", 60);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = windowSeconds.ToString(CultureInfo.InvariantCulture);
                IProblemDetailsService problemDetails = context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests.",
                    },
                });
            };
            options.AddPolicy(RateLimitPolicies.Public, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });
        return services;
    }
}
