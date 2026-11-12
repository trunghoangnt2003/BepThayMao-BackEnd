using System.Reflection;
using System.Net;
using Bepthaymao.Integration.Tests.Infrastructure;
namespace Bepthaymao.Integration.Tests.Security;

public sealed class RateLimitingTests
{
    [Fact]
    public void ApiExposesThePublicRateLimitPolicy()
    {
        Type? policyType = typeof(Program).Assembly.GetType("Bepthaymao.Api.Security.RateLimitPolicies");

        Assert.NotNull(policyType);
        Assert.Equal("public", policyType!.GetField("Public", BindingFlags.Public | BindingFlags.Static)?.GetValue(null));
    }

    [Fact]
    public async Task ThirdRequestReturnsProblemDetailsWithRetryAfter()
    {
        await using TestApp app = await TestApp.CreateAsync(2);
        HttpClient client = app.Client;
        await client.GetAsync("/limited", TestContext.Current.CancellationToken);
        await client.GetAsync("/limited", TestContext.Current.CancellationToken);
        using HttpResponseMessage response = await client.GetAsync("/limited", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("Retry-After"));
        Assert.Contains("correlationId", body, StringComparison.Ordinal);
    }
}
