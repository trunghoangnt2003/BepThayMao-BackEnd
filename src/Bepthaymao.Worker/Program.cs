HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
using IHost host = builder.Build();
await host.RunAsync();
