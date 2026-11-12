using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Bepthaymao.Api.Observability;

internal static class TelemetrySetup
{
    public static IServiceCollection AddApiTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("bepthaymao-api"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(options => options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health"));
                tracing.AddHttpClientInstrumentation();
                if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                    tracing.AddOtlpExporter(options => options.Endpoint = uri);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                    metrics.AddOtlpExporter(options => options.Endpoint = uri);
            });
        return services;
    }
}
