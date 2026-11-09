using Bepthaymao.Infrastructure.Observability;
using Serilog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog((services, loggerConfiguration) =>
    loggerConfiguration
        .ConfigureBepthaymao(builder.Configuration, "bepthaymao-worker")
        .ReadFrom.Services(services));
using IHost host = builder.Build();
await host.RunAsync();
