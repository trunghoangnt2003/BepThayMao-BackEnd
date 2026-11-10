using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Bepthaymao.Api.Observability;

internal static class TelemetrySetup
{
    public const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static IServiceCollection AddApiTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        // The OTLP exporter reads OTEL_EXPORTER_OTLP_ENDPOINT itself; attach it only when a collector is configured.
        bool exportOtlp = !string.IsNullOrWhiteSpace(configuration[OtlpEndpointVariable]);
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("bepthaymao-api"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(options =>
                        options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation();
                if (exportOtlp)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
                if (exportOtlp)
                {
                    metrics.AddOtlpExporter();
                }
            });
        return services;
    }
}
