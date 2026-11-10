using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Bepthaymao.Integration.Tests.Infrastructure;

internal sealed class ApiFactory(IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    // Port 1 is never listening: hosts start (required keys present) and readiness reports the dependency down.
    public const string ClosedPostgres = "Host=127.0.0.1;Port=1;Database=none;Timeout=1";

    private static readonly Dictionary<string, string?> _unreachable = new(StringComparer.Ordinal)
    {
        ["ConnectionStrings:Default"] = ClosedPostgres,
        ["ConnectionStrings:Redis"] = "127.0.0.1:1",
        ["ConnectionStrings:RabbitMq"] = "amqp://guest:guest@127.0.0.1:1/",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting (not ConfigureAppConfiguration) so Program sees values while registering services.
        foreach ((string key, string? value) in _unreachable.Concat(settings ?? new Dictionary<string, string?>()))
        {
            builder.UseSetting(key, value);
        }
    }
}
