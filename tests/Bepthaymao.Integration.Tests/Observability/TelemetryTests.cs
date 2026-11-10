using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Bepthaymao.Integration.Tests.Observability;

public sealed class TelemetryTests
{
    [Fact]
    public async Task TracingIsRegisteredWithoutOtlpEndpoint()
    {
        await using ApiFactory factory = new(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "" });
        using HttpClient client = factory.CreateClient();
        Assert.NotNull(factory.Services.GetService<TracerProvider>());
    }
}
