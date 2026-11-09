using Bepthaymao.Infrastructure.Observability;
using Serilog;

namespace Bepthaymao.Api.Observability;

internal static class LoggingSetup
{
    public static WebApplicationBuilder AddApiLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, loggerConfiguration) =>
            loggerConfiguration
                .ConfigureBepthaymao(builder.Configuration, "bepthaymao-api")
                .ReadFrom.Services(services));
        return builder;
    }
}
