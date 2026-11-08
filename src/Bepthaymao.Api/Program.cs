using Bepthaymao.Api.Health;
using Bepthaymao.Api.OpenApi;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddApiOpenApi();
builder.Services.AddApiHealthChecks();
WebApplication app = builder.Build();
app.NewVersionedApi("Bepthaymao").MapGroup("/api/v{version:apiVersion}").HasApiVersion(1.0);
app.MapOpenApi().WithDocumentPerVersion();
app.MapApiHealthEndpoints();
await app.RunAsync();
