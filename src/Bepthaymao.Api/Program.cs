using Bepthaymao.Api.Health;
using Bepthaymao.Api.Errors;
using Bepthaymao.Api.Observability;
using Bepthaymao.Api.Security;
using Bepthaymao.Api.OpenApi;
using Bepthaymao.Api.Endpoints.Branches;
using Bepthaymao.Api.Cli;
using Bepthaymao.Api.Configuration;
using Bepthaymao.Application;
using Bepthaymao.Infrastructure;

// Before CreateBuilder: build-time GetDocument.Insider passes --applicationName, never "db" (P5).
if (args is ["db", ..])
{
    return await DbCommand.RunAsync(args[1..]);
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddApiLogging();
builder.Services.AddApiErrorHandling();
builder.Services.AddApiForwardedHeaders(builder.Configuration);
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddApiOpenApi();
builder.Services.AddApiHealthChecks(builder.Configuration);
builder.Services.AddApiTelemetry(builder.Configuration);
IntegrationModeOptions.AddIntegrationModes(builder.Services, builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
WebApplication app = builder.Build();
app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseApiRequestLogging();
app.UseApiErrorHandling();
app.UseRouting();
app.UseRateLimiter();
RouteGroupBuilder api = app.NewVersionedApi("Bepthaymao").MapGroup("/api/v{version:apiVersion}").HasApiVersion(1.0);
api.MapPublicBranchesEndpoints();
app.MapOpenApi().WithDocumentPerVersion();
app.MapApiHealthEndpoints();
await app.RunAsync();
return 0;
