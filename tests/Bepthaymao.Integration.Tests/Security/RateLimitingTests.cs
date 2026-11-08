using System.Net;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Security;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task ThirdRequestIsRejectedWithProblemAndRetryAfter()
    {
        await using TestApp app = await TestApp.StartAsync(permitLimit: 2);
        using HttpResponseMessage first = await app.Client.GetAsync("/ok", TestContext.Current.CancellationToken);
        using HttpResponseMessage second = await app.Client.GetAsync("/ok", TestContext.Current.CancellationToken);
        using HttpResponseMessage third = await app.Client.GetAsync("/ok", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(third.Headers.RetryAfter);
        Assert.True(third.Headers.Contains("X-Correlation-Id"));
    }
}
