using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Auth;

public sealed class LoginTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    [Fact]
    public async Task ValidCredentialsReturnTokenPairAndRefreshCookie()
    {
        await using ApiFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
        });
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = "owner-test", password = "test-password-only" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(900, document.RootElement.GetProperty("expiresIn").GetInt32());
        Assert.Equal("Owner", document.RootElement.GetProperty("role").GetString());
        Assert.Equal("owner-test", document.RootElement.GetProperty("identifier").GetString());
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("refresh_token=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
