using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Bepthaymao.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bepthaymao.Integration.Tests.Security;

public sealed class JwtValidationTests
{
    private const string _signingKey = "test-signing-key-for-integration-tests-only-48-bytes";

    [Theory]
    [InlineData("valid", HttpStatusCode.OK)]
    [InlineData("missing", HttpStatusCode.Unauthorized)]
    [InlineData("tampered", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("algorithm", HttpStatusCode.Unauthorized)]
    [InlineData("issuer", HttpStatusCode.Unauthorized)]
    [InlineData("audience", HttpStatusCode.Unauthorized)]
    public async Task ValidatesSignatureLifetimeAlgorithmIssuerAndAudience(string scenario, HttpStatusCode expected)
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();
        if (scenario != "missing") client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(scenario));
        using HttpResponseMessage response = await client.GetAsync("/protected", TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCannotCallManagerEndpoint()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("valid"));
        using HttpResponseMessage response = await client.GetAsync("/manager", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short-key")]
    public async Task RejectsMissingOrShortSigningKeyAtStartup(string key)
    {
        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(async () =>
        {
            await using WebApplication app = await CreateAppAsync(key);
        });
    }

    private static async Task<WebApplication> CreateAppAsync(string key = _signingKey)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:SigningKey"] = key,
        });
        builder.Services.AddApiAuthentication(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/protected", () => "ok");
        app.MapGet("/manager", () => "ok").RequireAuthorization("ManagerOnly");
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static string Token(string scenario)
    {
        DateTime now = DateTime.UtcNow;
        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = scenario == "issuer" ? "other" : "test-issuer",
            Audience = scenario == "audience" ? "other" : "test-audience",
            Subject = new ClaimsIdentity([new("sub", Guid.CreateVersion7().ToString()), new("role", "Owner"), new("owner_id", Guid.CreateVersion7().ToString())]),
            IssuedAt = now.AddMinutes(-20),
            NotBefore = now.AddMinutes(-20),
            Expires = scenario == "expired" ? now.AddMinutes(-1) : now.AddMinutes(15),
            SigningCredentials = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey)),
                scenario == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256),
        };
        string token = new JsonWebTokenHandler().CreateToken(descriptor);
        if (scenario != "tampered") return token;
        int signature = token.LastIndexOf('.') + 1;
        return token[..signature] + (token[signature] == 'A' ? 'B' : 'A') + token[(signature + 1)..];
    }
}
