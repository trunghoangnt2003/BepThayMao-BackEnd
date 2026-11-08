using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Bepthaymao.Api.Health;

internal static class HealthSetup
{
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();
        return services;
    }

    public static IEndpointRouteBuilder MapApiHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .ExcludeFromDescription();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        }).ExcludeFromDescription();
        return endpoints;
    }
}
