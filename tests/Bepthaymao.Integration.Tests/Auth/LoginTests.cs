using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Integration.Tests.Infrastructure;
using Bepthaymao.Application.Auth;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bepthaymao.Domain.Branches;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Cryptography;
using System.Text;
using Bepthaymao.Api.Security;

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
        using IServiceScope scope = factory.Services.CreateSystemScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "owner-test", "test-password-only", UserRole.Owner, Guid.CreateVersion7());
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

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.BranchManager)]
    public async Task AccessTokenCarriesScopeClaims(UserRole role)
    {
        await using ApiFactory factory = CreateFactory(await infrastructure.CreateDatabaseAsync());
        using IServiceScope scope = factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Guid scopeId = Guid.CreateVersion7();
        if (role == UserRole.BranchManager)
        {
            Guid branchOwner = Guid.CreateVersion7();
            db.Owners.Add(Bepthaymao.Domain.Owners.Owner.Create(branchOwner, "Test owner", DateTimeOffset.UtcNow));
            db.Branches.Add(Branch.Create(scopeId, branchOwner, new("Branch", "Address", "0901234567", 10, 106, new(6, 0), new(21, 30), 10, [], null, null, null, true), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var user = await AuthTestData.CreateUserAsync(scope.ServiceProvider, "scope-test", "test-password-only", role,
            role == UserRole.Owner ? scopeId : null, role == UserRole.BranchManager ? scopeId : null);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = "scope-test", password = "test-password-only" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonWebToken token = new(json.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal(user.Id.ToString(), token.GetClaim("sub").Value);
        Assert.Equal("scope-test", token.GetClaim("name").Value);
        Assert.Equal(role.ToString(), token.GetClaim("role").Value);
        Assert.Equal(scopeId.ToString(), token.GetClaim(role == UserRole.Owner ? "owner_id" : "branch_id").Value);
        Assert.Equal("HS256", token.Alg);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.IssuedAt);
    }

    [Fact]
    public async Task RefreshTokenIsStoredHashed()
    {
        await using ApiFactory factory = CreateFactory(await infrastructure.CreateDatabaseAsync());
        using IServiceScope scope = factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var user = await AuthTestData.CreateUserAsync(scope.ServiceProvider, "hash-test", "test-password-only", UserRole.Owner, Guid.CreateVersion7());
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = "hash-test", password = "test-password-only" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        string raw = Uri.UnescapeDataString(cookie.Split(';')[0]["refresh_token=".Length..]);
        var stored = await db.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(user.Id, stored.UserId);
        Assert.Matches("^[a-f0-9]{64}$", stored.TokenHash);
        Assert.NotEqual(raw, stored.TokenHash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), stored.TokenHash);
        Assert.Equal(TimeSpan.FromDays(7), stored.ExpiresAt - stored.CreatedAt);
        Assert.Null(stored.RevokedAt);
    }

    private static ApiFactory CreateFactory(string connectionString) => new(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default"] = connectionString,
        ["RateLimiting:Public:PermitLimit"] = "1000",
    });

    [Fact]
    public async Task WrongPasswordAndUnknownUserReturnIdenticalProblem()
    {
        await using ApiFactory factory = CreateFactory(await infrastructure.CreateDatabaseAsync());
        using IServiceScope scope = factory.Services.CreateSystemScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "known-user", "test-password-only", UserRole.Owner, Guid.CreateVersion7());
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage wrong = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = "known-user", password = "wrong-password-only" }, TestContext.Current.CancellationToken);
        using HttpResponseMessage unknown = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = "unknown-user", password = "wrong-password-only" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal("application/problem+json", wrong.Content.Headers.ContentType?.MediaType);
        using JsonDocument first = JsonDocument.Parse(await wrong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using JsonDocument second = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(first.RootElement.GetProperty("title").GetString(), second.RootElement.GetProperty("title").GetString());
        Assert.Equal(first.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal),
            second.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.True(first.RootElement.TryGetProperty("correlationId", out _));
        Assert.True(second.RootElement.TryGetProperty("correlationId", out _));
        Assert.False(first.RootElement.TryGetProperty("detail", out _));
    }

    [Theory]
    [InlineData("{\"identifier\":\"owner\"}")]
    [InlineData("{\"identifier\":\"owner\",\"password\":\"123456789\"}")]
    [InlineData("{\"identifier\":\"owner\",\"password\":\"test-password-only\",\"role\":\"Owner\"}")]
    public async Task InvalidBodiesReturn400(string json)
    {
        await using ApiFactory factory = CreateFactory(await infrastructure.CreateDatabaseAsync());
        using HttpClient client = factory.CreateClient();
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/v1/auth/login", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task NoLockoutAfterRepeatedFailures()
    {
        await using ApiFactory factory = CreateFactory(await infrastructure.CreateDatabaseAsync());
        using IServiceScope scope = factory.Services.CreateSystemScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "no-lockout", "test-password-only", UserRole.Owner, Guid.CreateVersion7());
        using HttpClient client = factory.CreateClient();
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using HttpResponseMessage wrong = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { identifier = "no-lockout", password = "wrong-password-only" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }
        using HttpResponseMessage valid = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = "no-lockout", password = "test-password-only" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }
}
