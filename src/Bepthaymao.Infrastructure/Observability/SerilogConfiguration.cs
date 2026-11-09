using Serilog;
using Serilog.Formatting.Compact;
using Microsoft.Extensions.Configuration;

namespace Bepthaymao.Infrastructure.Observability;

public static class SerilogConfiguration
{
    public static LoggerConfiguration ConfigureBepthaymao(
        this LoggerConfiguration configuration,
        IConfiguration appConfiguration,
        string applicationName)
    {
        return configuration
            .ReadFrom.Configuration(appConfiguration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", applicationName)
            .WriteTo.Console(new CompactJsonFormatter());
    }
}
