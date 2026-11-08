using System.Net;
using System.Text.Json;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Errors;

public sealed class ErrorHandlingTests
{
    [Fact]
    public async Task UnknownExceptionReturnsGenericProblemWithoutPrivateDetails()
    {
        await using TestApp app = await TestApp.StartAsync();
        using HttpResponseMessage response = await app.Client.GetAsync("/fail", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("private-exception-detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(body);
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), document.RootElement.GetProperty("correlationId").GetString());
        Assert.Matches("^[a-f0-9]{32}$", document.RootElement.GetProperty("traceId").GetString() ?? "");
    }

    [Theory]
    [InlineData("/invalid", "items[0].openTime")]
    [InlineData("/domain", "openTime")]
    public async Task ValidationAndDomainErrorsPreserveJsonFieldPaths(string path, string key)
    {
        await using TestApp app = await TestApp.StartAsync();
        using HttpResponseMessage response = await app.Client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("errors").GetProperty(key).ValueKind);
        Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task MissingRouteReturnsProblemDetails()
    {
        await using TestApp app = await TestApp.StartAsync();
        using HttpResponseMessage response = await app.Client.GetAsync("/api/v1/khong-co", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("correlationId").GetString()));
    }
}
