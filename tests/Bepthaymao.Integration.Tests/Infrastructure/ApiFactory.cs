using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly InfrastructureFixture? _infrastructure;
    private readonly IReadOnlyDictionary<string, string?>? _settings;
    private readonly TimeProvider? _clock;

    public ApiFactory()
    {
    }

    internal ApiFactory(InfrastructureFixture infrastructure)
    {
        _infrastructure = infrastructure;
    }

    internal ApiFactory(IReadOnlyDictionary<string, string?> settings, TimeProvider? clock = null)
    {
        _settings = settings;
        _clock = clock;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-for-integration-tests-only-48-bytes");
        builder.UseSetting("RateLimiting:Login:PermitLimit", "1000");
        builder.UseSetting("RateLimiting:LoginIdentifier:PermitLimit", "1000");
        if (_clock is not null)
            builder.ConfigureTestServices(services => services.AddSingleton(_clock));
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
