using System.Net;
using System.Text.Json;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Branches;

[Collection(InfrastructureGroup.Name)]
public sealed class PublicBranchesEndpointTests(InfrastructureFixture infrastructure)
{
    private static readonly string[] _contractFields = ["address", "closeTime", "id", "name", "openTime", "phone"];

    [Fact]
    public async Task ReturnsOnlyActiveBranchesOrderedByNameWithSixFields()
    {
        string database = await infrastructure.CreateDatabaseAsync();
        await using (AppDbContext context = InfrastructureFixture.CreateContext(database))
        {
            context.Branches.AddRange(
                NewBranch("Cơ sở B", true, new TimeOnly(7, 0), new TimeOnly(21, 0)),
                NewBranch("Cơ sở Tắt", false, new TimeOnly(6, 0), new TimeOnly(21, 30)),
                NewBranch("Cơ sở A", true, new TimeOnly(6, 0), new TimeOnly(21, 30)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using ApiFactory factory = new(infrastructure.Settings(database));
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement[] items = [.. body.RootElement.EnumerateArray()];
        Assert.Equal(["Cơ sở A", "Cơ sở B"], items.Select(item => item.GetProperty("name").GetString()));
        Assert.All(items, item => Assert.Equal(_contractFields, item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        Assert.Equal("06:00", items[0].GetProperty("openTime").GetString());
        Assert.Equal("21:30", items[0].GetProperty("closeTime").GetString());
    }

    [Fact]
    public async Task ReturnsEmptyArrayWhenNoBranchIsActive()
    {
        string database = await infrastructure.CreateDatabaseAsync();
        await using ApiFactory factory = new(infrastructure.Settings(database));
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnreachableDatabaseReturnsProblemWithoutConnectionDetails()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("correlationId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThirdRequestOverPublicLimitReturns429Problem()
    {
        string database = await infrastructure.CreateDatabaseAsync();
        Dictionary<string, string?> settings = infrastructure.Settings(database);
        settings["RateLimiting:Public:PermitLimit"] = "2";
        await using ApiFactory factory = new(settings);
        using HttpClient client = factory.CreateClient();
        for (int request = 0; request < 2; request++)
        {
            using HttpResponseMessage allowed = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using HttpResponseMessage response = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("correlationId", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    internal static Branch NewBranch(string name, bool isActive, TimeOnly open, TimeOnly close) =>
        Branch.Create(Guid.CreateVersion7(), name, "Số 1 Đường Mẫu", "02800000001", open, close, isActive, DateTimeOffset.UtcNow);
}
