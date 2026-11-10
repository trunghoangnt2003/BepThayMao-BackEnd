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
        using IServiceScope scope = factory.Services.CreateScope();
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
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Guid scopeId = Guid.CreateVersion7();
        if (role == UserRole.BranchManager)
        {
            db.Branches.Add(Branch.Create(scopeId, "Branch", "Address", "0901234567", new(6, 0), new(21, 30), true, DateTimeOffset.UtcNow));
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
        using IServiceScope scope = factory.Services.CreateScope();
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
}
