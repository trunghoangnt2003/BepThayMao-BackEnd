using System.Net;
using System.Text.Json;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches.UpsertBranches;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Api.Cli;
using Bepthaymao.Domain.Owners;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Branches;

public sealed class PublicBranchesEndpointTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    [Fact]
    public async Task ReturnsOnlyActiveBranchesWithSixFieldsAndSeedIsIdempotent()
    {
        string connectionString = await infrastructure.CreateDatabaseAsync();
        await using ApiFactory factory = CreateFactory(connectionString);
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        SeedRunner runner = scope.ServiceProvider.GetRequiredService<SeedRunner>();
        SeedOptions options = new("public-owner", "Test owner", "test-seed-password", Path.Combine(RepositoryPaths.Root, "seed", "branches.sample.json"), "Development", null);
        Assert.Equal(new("created", 3, 0, 0, 0), await runner.RunAsync(options, TestContext.Current.CancellationToken));
        Assert.Equal(new("unchanged", 0, 0, 3, 0), await runner.RunAsync(options, TestContext.Current.CancellationToken));
        Assert.Equal(3, await db.Branches.CountAsync(TestContext.Current.CancellationToken));
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, document.RootElement.GetArrayLength());
        JsonElement first = document.RootElement[0];
        Assert.Equal("Cơ sở Mẫu 1", first.GetProperty("name").GetString());
        Assert.Equal("06:00", first.GetProperty("openTime").GetString());
        Assert.Equal(["address", "closeTime", "id", "name", "openTime", "phone"], first.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DatabaseEnforcesHoursAndFailedSeedDoesNotWritePartialChanges()
    {
        await using ApiFactory factory = CreateFactory(await infrastructure.CreateDatabaseAsync());
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Guid id = Guid.CreateVersion7();
        Guid ownerId = Guid.CreateVersion7();
        db.Owners.Add(Owner.Create(ownerId, "Test owner", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync($"INSERT INTO branches (id,owner_id,lat,lng,name,address,phone,open_time,close_time,is_active,created_at) VALUES ({id},{ownerId},10,106, 'Test', 'Test', '0901234567', TIME '21:00', TIME '06:00', true, CURRENT_TIMESTAMP)", TestContext.Current.CancellationToken));
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpsertBranchesCommand, UpsertBranchesResult>>();
        BranchUpsertItem valid = new(id, "Mẫu", "Mẫu", "0901234567", "06:00", "21:30", true);
        await Assert.ThrowsAsync<Domain.Common.DomainRuleException>(() => handler.Handle(new(ownerId, [valid, valid with { Id = Guid.CreateVersion7(), Phone = "bad" }]), TestContext.Current.CancellationToken));
        using IServiceScope freshScope = factory.Services.CreateScope();
        Assert.Equal(0, await freshScope.ServiceProvider.GetRequiredService<AppDbContext>().Branches.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyResponseAndRateLimitUseRealEndpoint()
    {
        await using ApiFactory factory = CreateFactory(await infrastructure.CreateDatabaseAsync());
        using IServiceScope scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        using HttpClient client = factory.CreateClient();
        Assert.Equal("[]", await client.GetStringAsync("/api/v1/branches", TestContext.Current.CancellationToken));
        using HttpResponseMessage second = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
        using HttpResponseMessage third = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.Contains("correlationId", await third.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private static ApiFactory CreateFactory(string connectionString) => new(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default"] = connectionString,
        ["RateLimiting:Public:PermitLimit"] = "2",
    });
}
