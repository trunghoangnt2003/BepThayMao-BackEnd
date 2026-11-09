using OpenTelemetry.Trace;
using Microsoft.Extensions.DependencyInjection;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Observability;

public sealed class TelemetryTests
{
    [Fact]
    public void TracerProviderIsRegisteredWithoutCollector()
    {
        using ApiFactory factory = new();
        _ = factory.Services.GetRequiredService<TracerProvider>();
    }
}
