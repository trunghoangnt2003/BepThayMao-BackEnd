using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Bepthaymao.Integration.Tests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly InfrastructureFixture? _infrastructure;
    private readonly IReadOnlyDictionary<string, string?>? _settings;

    public ApiFactory()
    {
    }

    internal ApiFactory(InfrastructureFixture infrastructure)
    {
        _infrastructure = infrastructure;
    }

    internal ApiFactory(IReadOnlyDictionary<string, string?> settings)
    {
        _settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-for-integration-tests-only-48-bytes");
        if (_infrastructure is not null)
        {
            builder.UseSetting("ConnectionStrings:Default", _infrastructure.PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", _infrastructure.RedisConnectionString);
            builder.UseSetting("ConnectionStrings:RabbitMq", _infrastructure.RabbitConnectionString);
        }
        else if (_settings is not null)
        {
            foreach ((string key, string? value) in _settings)
                builder.UseSetting(key, value);
        }
    }
}
