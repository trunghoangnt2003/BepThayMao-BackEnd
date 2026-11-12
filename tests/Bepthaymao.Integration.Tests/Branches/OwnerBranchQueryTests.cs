using System.Net;
using System.Text.Json;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Integration.Tests.Branches;

public sealed class OwnerBranchQueryTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    [Fact]
    public async Task ListReturnsOnlyOwnBranches()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner1 = await world.ClientForAsync(AuthSubject.Owner1);
        using HttpClient owner2 = await world.ClientForAsync(AuthSubject.Owner2);
        using HttpResponseMessage first = await owner1.GetAsync("/api/v1/owner/branches", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using JsonDocument firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        string[] expectedNames = ["Cơ sở A1", "Cơ sở A2"];
        Assert.Equal(expectedNames, firstJson.RootElement.EnumerateArray().Select(item => item.GetProperty("name").GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { world.BranchA1, world.BranchA2 }.Order(), firstJson.RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Order());
        using HttpResponseMessage second = await owner2.GetAsync("/api/v1/owner/branches", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using JsonDocument secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement only = Assert.Single(secondJson.RootElement.EnumerateArray());
        Assert.Equal(world.BranchB1, only.GetProperty("id").GetGuid());
        Assert.Equal("manager-b1", only.GetProperty("managerIdentifier").GetString());
    }

    [Fact]
    public async Task GetForeignBranchReturns404()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner1 = await world.ClientForAsync(AuthSubject.Owner1);
        using HttpResponseMessage foreign = await owner1.GetAsync($"/api/v1/owner/branches/{world.BranchB1}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("application/problem+json", foreign.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Not found.", problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out _));
        using HttpResponseMessage own = await owner1.GetAsync($"/api/v1/owner/branches/{world.BranchA1}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await own.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("manager-a1", json.RootElement.GetProperty("managerIdentifier").GetString());
        Assert.Equal(world.BranchA1, json.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ScopesDoNotLeakBetweenContexts()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        await using AppDbContext owner1 = world.OpenContext(new TestDataScope(false, world.Owner1Id, null));
        await using AppDbContext owner2 = world.OpenContext(new TestDataScope(false, world.Owner2Id, null));
        await using AppDbContext manager = world.OpenContext(new TestDataScope(false, null, world.BranchA1));
        await using AppDbContext system = world.OpenContext(new TestDataScope(true, null, null));
        Guid[] expectedOwner1 = new[] { world.BranchA1, world.BranchA2 }.Order().ToArray();
        for (int round = 0; round < 2; round++)
        {
            Assert.Equal(expectedOwner1, await IdsAsync(owner1));
            Assert.Equal([world.BranchB1], await IdsAsync(owner2));
            Assert.Equal([world.BranchA1], await IdsAsync(manager));
            Assert.Equal(3, (await IdsAsync(system)).Length);
        }
    }

    [Fact]
    public async Task AnonymousScopeSeesNothingButPublicListStillWorks()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        await using AppDbContext anonymous = world.OpenContext(new TestDataScope(false, null, null));
        Assert.Empty(await IdsAsync(anonymous));
        using HttpClient client = await world.ClientForAsync(AuthSubject.Anonymous);
        using HttpResponseMessage response = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, json.RootElement.GetArrayLength());
        Assert.Equal(["address", "closeTime", "id", "name", "openTime", "phone"],
            json.RootElement[0].EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal));
    }

    private static async Task<Guid[]> IdsAsync(AppDbContext db) =>
        await db.Branches.AsNoTracking().Select(branch => branch.Id).OrderBy(id => id).ToArrayAsync(TestContext.Current.CancellationToken);
}
