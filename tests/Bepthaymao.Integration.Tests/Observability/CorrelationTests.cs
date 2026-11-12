using Bepthaymao.Integration.Tests.Infrastructure;
using System.Net;
using System.Globalization;

namespace Bepthaymao.Integration.Tests.Observability;

public sealed class CorrelationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task LiveEndpointEchoesCorrelationHeader()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "abc-123");

        using HttpResponseMessage response = await factory.CreateClient().SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal("abc-123", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task InvalidCorrelationHeaderIsReplaced()
    {
        await using TestApp app = await TestApp.CreateAsync();
        using HttpRequestMessage request = new(HttpMethod.Get, "/ok");
        request.Headers.Add("X-Correlation-Id", new string('x', 65));

        using HttpResponseMessage response = await app.Client.SendAsync(
            request, TestContext.Current.CancellationToken);
        string correlationId = response.Headers.GetValues("X-Correlation-Id").Single();

        Assert.NotEqual(new string('x', 65), correlationId);
        Assert.InRange(correlationId.Length, 1, 64);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LogsContainCorrelationButNeverAuthorizationOrCookieValues()
    {
        await using TestApp app = await TestApp.CreateAsync();
        using HttpRequestMessage request = new(HttpMethod.Get, "/ok");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-secret-value");
        request.Headers.Add("Cookie", "session=private-cookie-value");

        using HttpResponseMessage response = await app.Client.SendAsync(
            request, TestContext.Current.CancellationToken);
        string rendered = string.Join(Environment.NewLine, app.Sink.Events.Select(logEvent =>
            logEvent.RenderMessage(CultureInfo.InvariantCulture)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(app.Sink.Events);
        Assert.Contains(app.Sink.Events, logEvent => logEvent.Properties.ContainsKey("CorrelationId"));
        Assert.DoesNotContain("test-secret-value", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("private-cookie-value", rendered, StringComparison.Ordinal);
    }
}
