using System.Threading.RateLimiting;
using System.Globalization;

namespace Bepthaymao.Api.Security;

public static class RateLimitPolicies
{
    public const string Public = "public";
    public const string Login = "login";
}

internal static class RateLimitingSetup
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        int permitLimit = configuration.GetValue("RateLimiting:Public:PermitLimit", 120);
        int windowSeconds = configuration.GetValue("RateLimiting:Public:WindowSeconds", 60);
        int loginPermitLimit = configuration.GetValue("RateLimiting:Login:PermitLimit", 10);
        int loginWindowSeconds = configuration.GetValue("RateLimiting:Login:WindowSeconds", 60);
        services.AddSingleton<LoginAttemptLimiter>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                int retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retry)
                    ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : windowSeconds;
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
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
            options.AddPolicy(RateLimitPolicies.Login, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = loginPermitLimit,
                    Window = TimeSpan.FromSeconds(loginWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });
        return services;
    }
}
