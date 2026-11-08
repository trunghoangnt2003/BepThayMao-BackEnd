using Bepthaymao.Infrastructure.Observability;
using Serilog;

namespace Bepthaymao.Api.Observability;

internal static class LoggingSetup
{
    public static WebApplicationBuilder AddApiLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, configuration) =>
            configuration.ConfigureBepthaymao(builder.Configuration, "bepthaymao-api")
                .ReadFrom.Services(services), preserveStaticLogger: true);
        return builder;
    }

    public static WebApplication UseApiRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options => options.Logger = app.Services.GetRequiredService<Serilog.ILogger>());
        return app;
    }
}
