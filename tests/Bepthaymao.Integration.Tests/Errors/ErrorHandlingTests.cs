using Bepthaymao.Integration.Tests.Infrastructure;
using System.Net;

namespace Bepthaymao.Integration.Tests.Errors;

public sealed class ErrorHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task MissingRouteReturnsProblemDetailsWithCorrelationId()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/khong-co");
        request.Headers.Add("X-Correlation-Id", "abc-123");

        using HttpResponseMessage response = await factory.CreateClient().SendAsync(
            request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("abc-123", response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Contains("\"correlationId\":\"abc-123\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledExceptionReturnsSafeProblemDetails()
    {
        await using TestApp app = await TestApp.CreateAsync();
        using HttpResponseMessage response = await app.Client.GetAsync(
            "/throw", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret exception detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", body, StringComparison.Ordinal);
        Assert.Contains("correlationId", body, StringComparison.Ordinal);
        Assert.Contains("traceId", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationAndDomainExceptionsReturnStructuredErrors()
    {
        await using TestApp app = await TestApp.CreateAsync();
        HttpClient client = app.Client;

        using HttpResponseMessage validation = await client.GetAsync("/validation", TestContext.Current.CancellationToken);
        string validationBody = await validation.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using HttpResponseMessage domain = await client.GetAsync("/domain", TestContext.Current.CancellationToken);
        string domainBody = await domain.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        Assert.Contains("items[0].openTime", validationBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, domain.StatusCode);
        Assert.Contains("openTime", domainBody, StringComparison.Ordinal);
        Assert.Contains("closed", domainBody, StringComparison.Ordinal);
    }
}
