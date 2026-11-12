using System.Net;
using System.Text.Json;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Integration.Tests.Branches;

public sealed class OwnerBranchQueryTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
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
