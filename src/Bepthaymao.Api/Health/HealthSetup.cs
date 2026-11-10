using System.Net.Sockets;
using Bepthaymao.Api.Hosting;
using Bepthaymao.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Bepthaymao.Api.Health;

internal static class HealthSetup
{
    public const string RedisConnectionName = "Redis";
    public const string RabbitMqConnectionName = "RabbitMq";
    private static readonly TimeSpan _checkTimeout = TimeSpan.FromSeconds(2);
    private static readonly string[] _readyTags = ["ready"];

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: _readyTags, timeout: _checkTimeout)
            .AddTypeActivatedCheck<TcpHealthCheck>("redis", null, _readyTags, _checkTimeout, RedisConnectionName)
            .AddTypeActivatedCheck<TcpHealthCheck>("rabbitmq", null, _readyTags, _checkTimeout, RabbitMqConnectionName);
        if (!BuildTimeDocs.IsActive)
        {
            services.AddOptions<RequiredConnectionStrings>().Bind(configuration.GetSection("ConnectionStrings"))
                .Validate(options => options.Missing().Count == 0,
                    $"Thiếu ConnectionStrings: cần có {DependencyInjection.ConnectionStringName}, {RedisConnectionName}, {RabbitMqConnectionName}.")
                .ValidateOnStart();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapApiHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .ExcludeFromDescription();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        }).ExcludeFromDescription();
        return endpoints;
    }
}

internal sealed class RequiredConnectionStrings
{
    public string? Default { get; set; }
    public string? Redis { get; set; }
    public string? RabbitMq { get; set; }

    public IReadOnlyList<string> Missing() =>
        [.. new[] { (Name: nameof(Default), Value: Default), (Name: nameof(Redis), Value: Redis), (Name: nameof(RabbitMq), Value: RabbitMq) }
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Name)];
}

internal sealed class PostgresHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString(DependencyInjection.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy();
        }

        try
        {
            await using NpgsqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using NpgsqlCommand command = new("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}

/// <summary>
/// shortcut: TCP reachability only (no AUTH or AMQP handshake), so no client library is added before
/// Redis/RabbitMQ are used; upgrade to a StackExchange.Redis PING / RabbitMQ.Client connect when a phase uses them.
/// </summary>
internal sealed class TcpHealthCheck(IConfiguration configuration, string connectionName) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseEndpoint(configuration.GetConnectionString(connectionName), out string host, out int port))
        {
            return HealthCheckResult.Unhealthy();
        }

        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }

    /// <summary>Accepts <c>amqp://user:pass@host:port/</c> or StackExchange style <c>host:port,password=...</c>.</summary>
    internal static bool TryParseEndpoint(string? connectionString, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        if (Uri.TryCreate(connectionString, UriKind.Absolute, out Uri? uri) && uri.Scheme is "amqp" or "amqps")
        {
            host = uri.Host;
            port = uri.Port > 0 ? uri.Port : uri.Scheme == "amqps" ? 5671 : 5672;
            return host.Length > 0;
        }

        string endpoint = connectionString.Split(',', 2)[0].Trim();
        int separator = endpoint.LastIndexOf(':');
        host = separator > 0 ? endpoint[..separator] : endpoint;
        port = separator > 0 && int.TryParse(endpoint[(separator + 1)..], out int parsed) ? parsed : 6379;
        return host.Length > 0;
    }
}
