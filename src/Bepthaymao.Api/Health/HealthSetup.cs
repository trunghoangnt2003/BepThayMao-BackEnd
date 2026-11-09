using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Bepthaymao.Api.Health;

internal static class HealthSetup
{
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var checks = services.AddHealthChecks();
        var postgres = configuration.GetConnectionString("Default");
        var redis = configuration.GetConnectionString("Redis");
        var rabbit = configuration.GetConnectionString("RabbitMq");
        if (!string.IsNullOrWhiteSpace(postgres)) checks.AddNpgSql(postgres, name: "postgres", tags: ["ready"]);
        if (!string.IsNullOrWhiteSpace(redis)) checks.AddRedis(redis, name: "redis", tags: ["ready"]);
        if (!string.IsNullOrWhiteSpace(rabbit))
            checks.AddRabbitMQ(async _ => await new ConnectionFactory { Uri = new Uri(rabbit) }.CreateConnectionAsync(), name: "rabbitmq", tags: ["ready"]);
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
