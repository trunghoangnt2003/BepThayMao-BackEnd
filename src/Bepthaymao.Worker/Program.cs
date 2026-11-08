using Bepthaymao.Infrastructure.Observability;
using Serilog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog((services, configuration) =>
    configuration.ConfigureBepthaymao(builder.Configuration, "bepthaymao-worker")
        .ReadFrom.Services(services), preserveStaticLogger: true);
using IHost host = builder.Build();
await host.RunAsync();
