using System.Net;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Health;

public sealed class HealthEndpointTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    [Fact]
    public async Task LiveReturnsSuccessWithoutInfrastructure()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyReturnsSuccessWhenInfrastructureIsUp()
    {
        await using ApiFactory factory = new(infrastructure);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyReturnsFailureWhenRunningPostgresStops()
    {
        await using ApiFactory factory = new(infrastructure);
        using HttpClient client = factory.CreateClient();
        await infrastructure.StopPostgresAsync();
        try
        {
            using HttpResponseMessage response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            using HttpResponseMessage live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
        finally { await infrastructure.StartPostgresAsync(); }
    }

    [Fact]
    public async Task ReadyReturnsFailureWhenInfrastructureIsUnavailable()
    {
        await using ApiFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing",
            ["ConnectionStrings:Redis"] = "127.0.0.1:1",
            ["ConnectionStrings:RabbitMq"] = "amqp://guest:guest@127.0.0.1:1/",
        });
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
