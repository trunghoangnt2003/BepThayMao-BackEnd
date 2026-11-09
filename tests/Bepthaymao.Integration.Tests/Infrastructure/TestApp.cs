using Bepthaymao.Api.Errors;
using Bepthaymao.Api.Observability;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Domain.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Bepthaymao.Integration.Tests.Infrastructure;

internal sealed class TestApp : IAsyncDisposable
{
    private TestApp(WebApplication app, CollectingSink sink, HttpClient client)
    {
        Application = app;
        Sink = sink;
        Client = client;
    }

    public WebApplication Application { get; }

    public CollectingSink Sink { get; }

    public HttpClient Client { get; }

    public static async Task<TestApp> CreateAsync(int permitLimit = 2)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(Program).Assembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Public:PermitLimit"] = permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:Public:WindowSeconds"] = "60",
        });
        CollectingSink sink = new();
        builder.Services.AddApiErrorHandling();
        builder.Services.AddApiForwardedHeaders(builder.Configuration);
        builder.Services.AddApiRateLimiting(builder.Configuration);
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        builder.Services.AddSerilog();

        WebApplication app = builder.Build();
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseApiErrorHandling();
        app.UseRateLimiter();
        app.MapGet("/throw", static IResult () => throw new InvalidOperationException("secret exception detail"));
        app.MapGet("/validation", static IResult () => throw new ValidationException(
            new Dictionary<string, string[]> { ["items[0].openTime"] = ["invalid"] }));
        app.MapGet("/domain", static IResult () => throw new DomainRuleException("openTime", "closed"));
        app.MapGet("/limited", static () => Results.Ok("ok")).RequireRateLimiting(RateLimitPolicies.Public);
        app.MapGet("/ok", static () => Results.Ok("ok"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        TestServer server = (TestServer)app.Services.GetRequiredService<IServer>();
        return new TestApp(app, sink, server.CreateClient());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Application.DisposeAsync();
    }
}
