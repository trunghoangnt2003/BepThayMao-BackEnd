using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Api.Cli;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Seeding;

public sealed class SeedTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    private static readonly string _sampleFile = Path.Combine(RepositoryPaths.Root, "seed", "branches.sample.json");
    private const string _password = "test-seed-only-password";

    [Fact]
    public async Task SeedCreatesOwnerWhoCanLogIn()
    {
        await using ApiFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
        });
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        SeedRunner runner = scope.ServiceProvider.GetRequiredService<SeedRunner>();
        SeedResult result = await runner.RunAsync(new("owner", "Chủ sở hữu", _password, _sampleFile, "Development", null), TestContext.Current.CancellationToken);
        Assert.Equal("created", result.Owner);
        Assert.Equal(3, result.Inserted);
        Guid ownerId = await db.Owners.Select(owner => owner.Id).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, await db.Branches.CountAsync(branch => branch.OwnerId == ownerId, TestContext.Current.CancellationToken));
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier = "owner", password = _password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Owner", json.RootElement.GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(91, 10, "ck_branches_lat")]
    [InlineData(10, 0, "ck_branches_abandoned_threshold")]
    public async Task DatabaseRejectsOutOfRangeValues(double lat, int threshold, string constraint)
    {
        await using ApiFactory factory = new(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync() });
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Guid ownerId = Guid.CreateVersion7();
        db.Owners.Add(Domain.Owners.Owner.Create(ownerId, "Test owner", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Guid id = Guid.CreateVersion7();
        Npgsql.PostgresException error = await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync($"INSERT INTO branches (id,owner_id,name,address,phone,lat,lng,abandoned_threshold_minutes,open_time,close_time,is_active,created_at) VALUES ({id},{ownerId},'Test','Test','0901234567',{lat},106,{threshold},TIME '06:00',TIME '21:30',true,CURRENT_TIMESTAMP)", TestContext.Current.CancellationToken));
        Assert.Equal(constraint, error.ConstraintName);
    }
}
