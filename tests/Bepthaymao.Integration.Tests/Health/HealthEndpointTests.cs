using System.Net;
using System.Text.Json;
using Bepthaymao.Integration.Tests.Infrastructure;
using Testcontainers.PostgreSql;

namespace Bepthaymao.Integration.Tests.Health;

[Collection(InfrastructureGroup.Name)]
public sealed class HealthEndpointTests(InfrastructureFixture infrastructure)
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
    public async Task ReadyReturns200WhenPostgresRedisRabbitMqAreUp()
    {
        string database = await infrastructure.CreateDatabaseAsync();
        await using ApiFactory factory = new(infrastructure.Settings(database));
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyReturns503WhenPostgresIsStoppedAndLiveStays200()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        // Own container so stopping it cannot affect other tests in the collection.
        await using PostgreSqlContainer postgres = new PostgreSqlBuilder(InfrastructureFixture.PostgresImage).Build();
        await postgres.StartAsync(cancellationToken);
        await using ApiFactory factory = new(infrastructure.Settings(postgres.GetConnectionString()));
        using HttpClient client = factory.CreateClient();
        using (HttpResponseMessage before = await client.GetAsync("/health/ready", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        await postgres.StopAsync(cancellationToken);

        using HttpResponseMessage ready = await client.GetAsync("/health/ready", cancellationToken);
        using HttpResponseMessage live = await client.GetAsync("/health/live", cancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public void HealthEndpointsAreNotInOpenApiDocument()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root, "openapi", "v1.json")));
        string[] paths = [.. document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name)];
        Assert.DoesNotContain(paths, path => path.StartsWith("/health", StringComparison.Ordinal));
    }
}
