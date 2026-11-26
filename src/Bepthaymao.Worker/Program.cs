using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Infrastructure;
using Bepthaymao.Infrastructure.Observability;
using Bepthaymao.Infrastructure.Orders;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Worker;
using Serilog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog((services, loggerConfiguration) =>
    loggerConfiguration
        .ConfigureBepthaymao(builder.Configuration, "bepthaymao-worker")
        .ReadFrom.Services(services));
// Worker chạy trong phạm vi hệ thống tường minh và không có phiên người dùng (T-03-05).
builder.Services.AddSingleton<IDataScope, SystemDataScope>();
builder.Services.AddScoped<ICurrentUser, WorkerCurrentUser>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<OrderExpirySweeper>();
builder.Services.AddSingleton<AbandonedOrderSweeper>();
builder.Services.AddHostedService<OrderExpiryWorker>();
builder.Services.AddHostedService<AbandonedOrderWorker>();
builder.Services.AddHostedService<PaymentWorker>();
builder.Services.AddHostedService<NotifierWorker>();
using IHost host = builder.Build();
await host.RunAsync();
