using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Security;

public sealed class CorsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CrossOriginResponseDoesNotGrantCorsAccess()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", "https://evil.example");

        using HttpResponseMessage response = await factory.CreateClient().SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
