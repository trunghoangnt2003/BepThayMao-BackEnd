using System.Globalization;
using System.Threading.RateLimiting;
using Bepthaymao.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Security;

internal static class RateLimitPolicies
{
    public const string Public = "public";
}

internal static class RateLimitingSetup
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        int permitLimit = configuration.GetValue("RateLimiting:Public:PermitLimit", 120);
        int windowSeconds = configuration.GetValue("RateLimiting:Public:WindowSeconds", 60);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSeconds);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicies.Public, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                HttpContext context = rejected.HttpContext;
                double retrySeconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retry)
                    ? Math.Ceiling(retry.TotalSeconds) : windowSeconds;
                context.Response.Headers.RetryAfter = retrySeconds.ToString(CultureInfo.InvariantCulture);
                await ErrorHandlingSetup.WriteProblemAsync(context,
                    context.RequestServices.GetRequiredService<IProblemDetailsService>(),
                    new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Quá nhiều yêu cầu." },
                    cancellationToken);
            };
        });
        return services;
    }
}
