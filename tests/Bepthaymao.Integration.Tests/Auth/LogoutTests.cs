using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Application.Auth;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bepthaymao.Api.Security;

namespace Bepthaymao.Integration.Tests.Auth;

public sealed class LogoutTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    [Fact]
    public async Task LogoutRevokesAllAndClearsCookie()
    {
        await using ApiFactory factory = await CreateFactory();
        using HttpClient client = factory.CreateClient();
        string first = await Login(client);
        string other = await Login(client);
        using HttpResponseMessage response = await Send(client, "/api/v1/auth/logout", first);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        string deleted = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("refresh_token=;", deleted, StringComparison.Ordinal);
        Assert.Contains("path=/api/v1/auth", deleted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", deleted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", deleted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", deleted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", deleted, StringComparison.OrdinalIgnoreCase);
        using HttpResponseMessage rejectedFirst = await Send(client, "/api/v1/auth/refresh", first);
        using HttpResponseMessage rejectedOther = await Send(client, "/api/v1/auth/refresh", other);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedFirst.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedOther.StatusCode);
        using IServiceScope scope = factory.Services.CreateSystemScope();
        Assert.All(await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.ToArrayAsync(TestContext.Current.CancellationToken),
            token => Assert.NotNull(token.RevokedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("refresh_token=invalid")]
    public async Task LogoutWithoutValidCookieIs204(string? cookie)
    {
        await using ApiFactory factory = await CreateFactory();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await Send(client, "/api/v1/auth/logout", cookie);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/logout")]
    public async Task ForeignOriginIsRejected(string path)
    {
        await using ApiFactory factory = await CreateFactory();
        using HttpClient client = factory.CreateClient();
        string cookie = await Login(client);
        using HttpResponseMessage rejected = await Send(client, path, cookie, "https://evil.example.com");
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        using JsonDocument json = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(json.RootElement.TryGetProperty("correlationId", out _));
        using HttpResponseMessage allowed = await Send(client, path, cookie, "https://admin.example.test");
        Assert.Equal(path.EndsWith("refresh", StringComparison.Ordinal) ? HttpStatusCode.OK : HttpStatusCode.NoContent, allowed.StatusCode);
    }

    private async Task<ApiFactory> CreateFactory()
    {
        ApiFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
            ["Auth:AllowedOrigins:0"] = "https://admin.example.test",
        });
        using IServiceScope scope = factory.Services.CreateSystemScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "logout-test", "test-password-only", UserRole.Owner, Guid.CreateVersion7());
        return factory;
    }

    private static async Task<string> Login(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier = "logout-test", password = "test-password-only" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, string? cookie, string? origin = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path);
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
