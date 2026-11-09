using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Bepthaymao.Integration.Tests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly InfrastructureFixture? _infrastructure;

    public ApiFactory()
    {
    }

    internal ApiFactory(InfrastructureFixture infrastructure)
    {
        _infrastructure = infrastructure;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, configuration) =>
        {
            if (_infrastructure is null) return;
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _infrastructure.PostgresConnectionString,
                ["ConnectionStrings:Redis"] = _infrastructure.RedisConnectionString,
                ["ConnectionStrings:RabbitMq"] = _infrastructure.RabbitConnectionString,
            });
        });
    }
}
