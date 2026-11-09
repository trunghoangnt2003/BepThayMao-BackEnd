using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Bepthaymao.Integration.Tests.Infrastructure;

public sealed class InfrastructureFixture : IAsyncLifetime
{
    public Task<string> CreateDatabaseAsync()
    {
        Npgsql.NpgsqlConnectionStringBuilder builder = new(PostgresConnectionString)
        {
            Database = "test_" + Guid.NewGuid().ToString("N"),
        };
        return Task.FromResult(builder.ConnectionString);
    }
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.10.3-alpine").Build();
    private readonly IContainer _rabbit = new ContainerBuilder("rabbitmq:4.4-management-alpine")
        .WithPortBinding(5672, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
        .Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();
    public string RabbitConnectionString => $"amqp://guest:guest@{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}/";
    public Task StopPostgresAsync() => _postgres.StopAsync(TestContext.Current.CancellationToken);
    public Task StartPostgresAsync() => _postgres.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await _redis.StartAsync(TestContext.Current.CancellationToken);
        await _rabbit.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _rabbit.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
