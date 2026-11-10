using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Application.Auth;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Security;

public sealed class LoginRateLimitTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    [Fact]
    public async Task IpPolicyReturns429ProblemDetails()
    {
        await using ApiFactory factory = await CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:PermitLimit"] = "3",
            ["RateLimiting:Login:WindowSeconds"] = "60",
        });
        using HttpClient client = factory.CreateClient();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpResponseMessage response = await Login(client, "test-user", "wrong-password-only");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using HttpResponseMessage rejected = await Login(client, "other-user", "wrong-password-only");
        await AssertLimited(rejected);
    }

    [Fact]
    public async Task IdentifierLimiterReturns429()
    {
        await using ApiFactory factory = await CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:LoginIdentifier:PermitLimit"] = "2",
            ["RateLimiting:LoginIdentifier:WindowSeconds"] = "60",
        });
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage first = await Login(client, "test-user", "wrong-password-only");
        using HttpResponseMessage second = await Login(client, "TEST-USER", "wrong-password-only");
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        using HttpResponseMessage rejected = await Login(client, " test-user ", "wrong-password-only");
        await AssertLimited(rejected);
        using HttpResponseMessage different = await Login(client, "another-user", "wrong-password-only");
        Assert.Equal(HttpStatusCode.Unauthorized, different.StatusCode);
    }

    [Fact]
    public async Task CorrectPasswordWorksAfterWindow()
    {
        await using ApiFactory factory = await CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:LoginIdentifier:PermitLimit"] = "1",
            ["RateLimiting:LoginIdentifier:WindowSeconds"] = "1",
        });
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage first = await Login(client, "test-user", "wrong-password-only");
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        using HttpResponseMessage limited = await Login(client, "test-user", "test-password-only");
        await AssertLimited(limited);
        await Task.Delay(TimeSpan.FromMilliseconds(1300), TestContext.Current.CancellationToken);
        using HttpResponseMessage valid = await Login(client, "test-user", "test-password-only");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    private async Task<ApiFactory> CreateFactory(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync();
        ApiFactory factory = new(settings);
        using IServiceScope scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "test-user", "test-password-only", UserRole.Owner, Guid.CreateVersion7());
        return factory;
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string identifier, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { identifier, password }, TestContext.Current.CancellationToken);

    private static async Task AssertLimited(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(int.Parse(Assert.Single(response.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture) >= 1);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(json.RootElement.TryGetProperty("correlationId", out _));
        Assert.Equal("Too many requests.", json.RootElement.GetProperty("title").GetString());
    }
}
