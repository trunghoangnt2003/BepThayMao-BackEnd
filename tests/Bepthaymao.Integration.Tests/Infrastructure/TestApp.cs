using System.Globalization;
using Bepthaymao.Api.Errors;
using Bepthaymao.Api.Observability;
using Bepthaymao.Api.Security;
using Bepthaymao.Domain.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;
using ValidationException = Bepthaymao.Application.Abstractions.Errors.ValidationException;

namespace Bepthaymao.Integration.Tests.Infrastructure;

internal sealed class TestApp : IAsyncDisposable
{
    private readonly WebApplication _application;

    private TestApp(WebApplication application, CollectingSink sink)
    {
        _application = application;
        Client = application.GetTestClient();
        Sink = sink;
    }

    public HttpClient Client { get; }
    public CollectingSink Sink { get; }

    public static async Task<TestApp> StartAsync(int permitLimit = 120)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Public:PermitLimit"] = permitLimit.ToString(CultureInfo.InvariantCulture),
            ["RateLimiting:Public:WindowSeconds"] = "60",
        });
        CollectingSink sink = new();
        builder.Services.AddSingleton<ILogEventSink>(sink);
        builder.AddApiLogging();
        builder.Services.AddApiErrorHandling();
        builder.Services.AddApiRateLimiting(builder.Configuration);
        WebApplication application = builder.Build();
        application.UseMiddleware<CorrelationIdMiddleware>();
        application.UseApiRequestLogging();
        application.UseApiErrorHandling();
        application.UseRouting();
        application.UseRateLimiter();
        application.MapGet("/ok", () => "ok").RequireRateLimiting(RateLimitPolicies.Public);
        application.MapGet("/fail", () => Throw(new InvalidOperationException("private-exception-detail")));
        application.MapGet("/invalid", () => Throw(new ValidationException(new Dictionary<string, string[]> { ["items[0].openTime"] = ["Required"] })));
        application.MapGet("/domain", () => Throw(new DomainRuleException("openTime", "Giờ không hợp lệ.")));
        await application.StartAsync(TestContext.Current.CancellationToken);
        return new TestApp(application, sink);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _application.DisposeAsync();
    }

    private static IResult Throw(Exception exception) => throw exception;
}
