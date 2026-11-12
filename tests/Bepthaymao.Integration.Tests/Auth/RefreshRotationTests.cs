using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Application.Auth;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Bepthaymao.Api.Security;

namespace Bepthaymao.Integration.Tests.Auth;

public sealed class RefreshRotationTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    [Fact]
    public async Task RefreshRotatesToken()
    {
        await using ApiFactory factory = await CreateFactory();
        using HttpClient client = factory.CreateClient();
        string cookie = await Login(client);
        using HttpResponseMessage response = await Refresh(client, cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string next = Cookie(response);
        Assert.NotEqual(cookie, next);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(900, json.RootElement.GetProperty("expiresIn").GetInt32());
        Assert.Equal(["accessToken", "expiresIn", "identifier", "role"], json.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        using IServiceScope scope = factory.Services.CreateSystemScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.OrderBy(value => value.CreatedAt).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, tokens.Length);
        Assert.NotNull(tokens[0].RevokedAt);
        Assert.Equal(tokens[1].Id, tokens[0].ReplacedById);
        Assert.Equal(tokens[0].FamilyId, tokens[1].FamilyId);
        Assert.Null(tokens[1].RevokedAt);
    }

    [Fact]
    public async Task RefreshReadsCurrentClaims()
    {
        await using ApiFactory factory = await CreateFactory(manager: true);
        using HttpClient client = factory.CreateClient();
        string cookie = await Login(client);
        Guid newBranch = Guid.CreateVersion7();
        using (IServiceScope scope = factory.Services.CreateSystemScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Guid branchOwner = Guid.CreateVersion7();
            db.Owners.Add(Bepthaymao.Domain.Owners.Owner.Create(branchOwner, "Test owner", DateTimeOffset.UtcNow));
            db.Branches.Add(Branch.Create(newBranch, branchOwner, new("New branch", "Address", "0901234567", 10, 106, new(6, 0), new(21, 30), 10, [], null, null, null, true), DateTimeOffset.UtcNow));
            var user = await db.Users.SingleAsync(TestContext.Current.CancellationToken);
            user.BranchId = newBranch;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        using HttpResponseMessage response = await Refresh(client, cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonWebToken token = new(json.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal(newBranch.ToString(), token.GetClaim("branch_id").Value);
    }

    [Fact]
    public async Task MissingOrExpiredTokenReturns401()
    {
        TestClock clock = new();
        await using ApiFactory factory = await CreateFactory(clock: clock);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage missing = await Refresh(client, null);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.False(missing.Headers.Contains("WWW-Authenticate"));
        string cookie = await Login(client);
        clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));
        using HttpResponseMessage expired = await Refresh(client, cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefreshCannotCreateTwoSuccessors()
    {
        await using ApiFactory factory = await CreateFactory();
        using HttpClient client = factory.CreateClient();
        string cookie = await Login(client);
        HttpResponseMessage[] responses = await Task.WhenAll(Refresh(client, cookie), Refresh(client, cookie));
        try
        {
            Assert.Single(responses, value => value.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, value => value.StatusCode == HttpStatusCode.Unauthorized);
            using IServiceScope scope = factory.Services.CreateSystemScope();
            var tokens = await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.ToArrayAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, tokens.Length);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task ReusedTokenRevokesAllUserSessions()
    {
        await using ApiFactory factory = await CreateFactory();
        using HttpClient client = factory.CreateClient();
        string first = await Login(client);
        string otherSession = await Login(client);
        using HttpResponseMessage rotated = await Refresh(client, first);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        string successor = Cookie(rotated);
        using HttpResponseMessage replay = await Refresh(client, first);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using HttpResponseMessage rejectedSuccessor = await Refresh(client, successor);
        using HttpResponseMessage rejectedOtherSession = await Refresh(client, otherSession);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedSuccessor.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedOtherSession.StatusCode);
        using IServiceScope scope = factory.Services.CreateSystemScope();
        Assert.All(await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.ToArrayAsync(TestContext.Current.CancellationToken),
            token => Assert.NotNull(token.RevokedAt));
    }

    private async Task<ApiFactory> CreateFactory(bool manager = false, TimeProvider? clock = null)
    {
        ApiFactory factory = new(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync() }, clock);
        using IServiceScope scope = factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Guid branch = Guid.CreateVersion7();
        if (manager)
        {
            Guid branchOwner = Guid.CreateVersion7();
            db.Owners.Add(Bepthaymao.Domain.Owners.Owner.Create(branchOwner, "Test owner", DateTimeOffset.UtcNow));
            db.Branches.Add(Branch.Create(branch, branchOwner, new("Branch", "Address", "0901234567", 10, 106, new(6, 0), new(21, 30), 10, [], null, null, null, true), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "refresh-test", "test-password-only", manager ? UserRole.BranchManager : UserRole.Owner,
            manager ? null : Guid.CreateVersion7(), manager ? branch : null);
        return factory;
    }

    private static async Task<string> Login(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier = "refresh-test", password = "test-password-only" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Cookie(response);
    }

    private static string Cookie(HttpResponseMessage response) => Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];

    private static async Task<HttpResponseMessage> Refresh(HttpClient client, string? cookie)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/refresh");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class TestClock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }
}
