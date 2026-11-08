using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Events;

namespace Bepthaymao.Infrastructure.Observability;

public static class SerilogConfiguration
{
    public static LoggerConfiguration ConfigureBepthaymao(this LoggerConfiguration configuration,
        IConfiguration appConfiguration, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(appConfiguration);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return configuration.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .ReadFrom.Configuration(appConfiguration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", applicationName)
            .WriteTo.Console(new CompactJsonFormatter());
    }
}
