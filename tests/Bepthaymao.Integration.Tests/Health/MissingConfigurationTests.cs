using System.Net;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Health;

public sealed class MissingConfigurationTests
{
    [Fact]
    public async Task ReadyFailsWhenRequiredConnectionStringsAreMissing()
    {
        await using ApiFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "",
            ["ConnectionStrings:Redis"] = "",
            ["ConnectionStrings:RabbitMq"] = "",
        });
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using HttpResponseMessage live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
