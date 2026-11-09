using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Bepthaymao.Integration.Tests.Infrastructure;

public sealed class InfrastructureFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.10.3-alpine").Build();
    private readonly IContainer _rabbit = new ContainerBuilder("rabbitmq:4.4-management-alpine")
        .WithPortBinding(5672, true)
        .Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();
    public string RabbitConnectionString => $"amqp://guest:guest@{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}/";

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
