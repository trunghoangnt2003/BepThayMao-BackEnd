using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        Type? runnerType = typeof(Program).Assembly.GetType("Bepthaymao.Api.Cli.SeedRunner");
        Type? optionsType = typeof(Program).Assembly.GetType("Bepthaymao.Api.Cli.SeedOptions");
        Assert.NotNull(runnerType);
        Assert.NotNull(optionsType);
        dynamic runner = scope.ServiceProvider.GetRequiredService(runnerType);
        dynamic options = Activator.CreateInstance(optionsType, "owner", "Chủ sở hữu", _password, _sampleFile, "Development", null)!;
        dynamic result = await runner.RunAsync(options, TestContext.Current.CancellationToken);
        Assert.Equal("created", result.Owner);
        Assert.Equal(3, result.Inserted);
        Guid ownerId = await db.Owners.Select(owner => owner.Id).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, await db.Branches.CountAsync(branch => EF.Property<Guid>(branch, "OwnerId") == ownerId, TestContext.Current.CancellationToken));
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier = "owner", password = _password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Owner", json.RootElement.GetProperty("role").GetString());
    }
}
