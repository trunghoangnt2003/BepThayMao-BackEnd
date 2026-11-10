using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Bepthaymao.Integration.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class InfrastructureGroup : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "infrastructure";
}

/// <summary>One PostgreSQL, Redis and RabbitMQ per test run (D-16 image tags); each test class gets its own database.</summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:18.6-alpine";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresImage).Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.10.3-alpine").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.4-management-alpine").Build();

    public string RedisConnectionString => _redis.GetConnectionString();
    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public async ValueTask InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbitMq.StartAsync());

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }

    /// <summary>
    /// Returns a connection string to a fresh database name. With <paramref name="migrate"/> the database is created
    /// (MigrateAsync creates it) and fully migrated; without it the database does not exist yet.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(bool migrate = true)
    {
        string connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = $"test_{Guid.NewGuid():N}",
        }.ConnectionString;
        if (migrate)
        {
            await using AppDbContext context = CreateContext(connectionString);
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return connectionString;
    }

    public Dictionary<string, string?> Settings(string postgresConnectionString) => new(StringComparer.Ordinal)
    {
        ["ConnectionStrings:Default"] = postgresConnectionString,
        ["ConnectionStrings:Redis"] = RedisConnectionString,
        ["ConnectionStrings:RabbitMq"] = RabbitMqConnectionString,
    };

    public static AppDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
}
