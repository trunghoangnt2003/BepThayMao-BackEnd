using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Security;

public sealed class CorsTests
{
    [Fact]
    public async Task UntrustedOriginDoesNotReceiveCorsPermission()
    {
        await using TestApp app = await TestApp.StartAsync();
        app.Client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        using HttpResponseMessage response = await app.Client.GetAsync("/ok", TestContext.Current.CancellationToken);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
