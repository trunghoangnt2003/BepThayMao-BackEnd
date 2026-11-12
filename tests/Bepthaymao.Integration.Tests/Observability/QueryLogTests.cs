using System.Globalization;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Bepthaymao.Integration.Tests.Observability;

public sealed class QueryLogTests
{
    [Fact]
    public async Task RealHostDoesNotLogSensitiveQueryValues()
    {
        CollectingSink sink = new();
        using ApiFactory factory = new();
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink)));
        using HttpClient client = host.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(
            "/health/live?phone=synthetic-private-phone", TestContext.Current.CancellationToken);
        string logs = string.Join('\n', sink.Events.Select(item => item.RenderMessage(CultureInfo.InvariantCulture)));
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("/health/live", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-private-phone", logs, StringComparison.Ordinal);
    }
}
