using System.Text.Json;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Observability;

public sealed class CorrelationTests
{
    [Theory]
    [InlineData("abc-123", true)]
    [InlineData("bad,value", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    public async Task CorrelationIsValidatedAndMatchesHeaderAndBody(string supplied, bool accepted)
    {
        await using TestApp app = await TestApp.StartAsync();
        app.Client.DefaultRequestHeaders.Add("X-Correlation-Id", supplied);
        using HttpResponseMessage response = await app.Client.GetAsync("/fail", TestContext.Current.CancellationToken);
        string id = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.Equal(accepted, id == supplied);
        Assert.Matches("^[A-Za-z0-9._-]{1,64}$", id);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(id, document.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task JsonLogsHaveCorrelationButNoAuthorizationOrCookieValues()
    {
        await using TestApp app = await TestApp.StartAsync();
        app.Client.DefaultRequestHeaders.Add("X-Correlation-Id", "abc-123");
        app.Client.DefaultRequestHeaders.Add("Authorization", "Bearer test-secret-value");
        app.Client.DefaultRequestHeaders.Add("Cookie", "session=private-cookie-value");
        using HttpResponseMessage response = await app.Client.GetAsync("/ok?phone=private-phone-value", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        string logs = app.Sink.Render();
        Assert.Contains("\"CorrelationId\":\"abc-123\"", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("test-secret-value", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("private-cookie-value", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("private-phone-value", logs, StringComparison.Ordinal);
    }
}
