using System.Net;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Health;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task LiveReturnsSuccessWithoutInfrastructure()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
