using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using Bepthaymao.Api.Cli;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

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

    [Fact]
    public async Task SecondRunChangesNothing()
    {
        await using ApiFactory factory = await CreateFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        SeedRunner runner = scope.ServiceProvider.GetRequiredService<SeedRunner>();
        SeedOptions options = new("owner", "Owner", _password, _sampleFile, "Development", null);
        await runner.RunAsync(options, TestContext.Current.CancellationToken);
        string? hash = await db.Users.Select(user => user.PasswordHash).SingleAsync(TestContext.Current.CancellationToken);
        SeedResult second = await runner.RunAsync(options with { OwnerPassword = "different-test-password" }, TestContext.Current.CancellationToken);
        Assert.Equal(new("unchanged", 0, 0, 3, 0), second);
        Assert.Equal(hash, await db.Users.Select(user => user.PasswordHash).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, await db.Branches.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("123456789")]
    public async Task MissingOwnerPasswordIsRejectedWithoutWrites(string? password)
    {
        await using ApiFactory factory = await CreateFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        SeedRunner runner = scope.ServiceProvider.GetRequiredService<SeedRunner>();
        await Assert.ThrowsAsync<SeedConfigurationException>(() => runner.RunAsync(new("owner", "Owner", password, _sampleFile, "Development", null), TestContext.Current.CancellationToken));
        await AssertNoWrites(factory);
    }

    [Fact]
    public async Task SampleFileSkippedOutsideDevelopment()
    {
        await using ApiFactory factory = await CreateFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        SeedResult result = await scope.ServiceProvider.GetRequiredService<SeedRunner>().RunAsync(new("owner", "Owner", _password, _sampleFile, "Production", null), TestContext.Current.CancellationToken);
        Assert.Equal(0, result.Inserted);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Owners.CountAsync(TestContext.Current.CancellationToken));
        // CLI must make the skip visible; verify result metadata without requiring a console logger.
        Assert.Contains("bỏ qua cơ sở mẫu", result.Note ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealFileCanBeSeededInProduction()
    {
        string path = await WriteSeedFile(sample: false);
        try
        {
            await using ApiFactory factory = await CreateFactory();
            using IServiceScope scope = factory.Services.CreateScope();
            SeedResult result = await scope.ServiceProvider.GetRequiredService<SeedRunner>().RunAsync(new("owner", "Owner", _password, path, "Production", null), TestContext.Current.CancellationToken);
            Assert.Equal(3, result.Inserted);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ManagerIdentifierCreatesManagerOnce()
    {
        string path = await WriteSeedFile(sample: false, manager: true);
        try
        {
            await using ApiFactory factory = await CreateFactory();
            using IServiceScope scope = factory.Services.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            SeedRunner runner = scope.ServiceProvider.GetRequiredService<SeedRunner>();
            SeedOptions options = new("owner", "Owner", _password, path, "Development", "test-manager-password");
            Assert.Equal(1, (await runner.RunAsync(options, TestContext.Current.CancellationToken)).Managers);
            var manager = await db.Users.SingleAsync(user => user.UserName == "seed-manager", TestContext.Current.CancellationToken);
            string? passwordHash = manager.PasswordHash;
            Assert.Equal(0, (await runner.RunAsync(options with { ManagerPassword = null }, TestContext.Current.CancellationToken)).Managers);
            Assert.Equal(passwordHash, manager.PasswordHash);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier = "seed-manager", password = "test-manager-password" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("BranchManager", json.RootElement.GetProperty("role").GetString());
            JsonWebToken jwt = new(json.RootElement.GetProperty("accessToken").GetString());
            Assert.Equal(manager.BranchId.ToString(), jwt.GetClaim("branch_id").Value);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MissingManagerPasswordLeavesNoWrites()
    {
        string path = await WriteSeedFile(sample: false, manager: true);
        try
        {
            await using ApiFactory factory = await CreateFactory();
            using IServiceScope scope = factory.Services.CreateScope();
            await Assert.ThrowsAsync<SeedConfigurationException>(() => scope.ServiceProvider.GetRequiredService<SeedRunner>().RunAsync(new("owner", "Owner", _password, path, "Development", null), TestContext.Current.CancellationToken));
            await AssertNoWrites(factory);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FailureLeavesNoPartialWrites()
    {
        string path = await WriteSeedFile(sample: false, invalidPhone: true);
        try
        {
            await using ApiFactory factory = await CreateFactory();
            using IServiceScope scope = factory.Services.CreateScope();
            await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<SeedRunner>().RunAsync(new("owner", "Owner", _password, path, "Development", null), TestContext.Current.CancellationToken));
            await AssertNoWrites(factory);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CliReturnsConfigurationExitCodeWithoutPrintingPassword()
    {
        await using ApiFactory factory = await CreateFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        string connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        ProcessStartInfo start = new("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepositoryPaths.Root };
        foreach (string argument in new[] { typeof(Program).Assembly.Location, "db", "seed", "--file", _sampleFile }) start.ArgumentList.Add(argument);
        start.Environment["ConnectionStrings__Default"] = connectionString;
        start.Environment["SEED_OWNER_PASSWORD"] = "123456789";
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        string output = await stdout + await stderr;
        Assert.Equal(2, process.ExitCode);
        Assert.DoesNotContain("123456789", output, StringComparison.Ordinal);
        await AssertNoWrites(factory);
    }

    private async Task<ApiFactory> CreateFactory()
    {
        ApiFactory factory = new(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync() });
        using IServiceScope scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        return factory;
    }

    private static async Task AssertNoWrites(ApiFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Owners.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.Branches.CountAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<string> WriteSeedFile(bool sample, bool manager = false, bool invalidPhone = false)
    {
        JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(_sampleFile, TestContext.Current.CancellationToken))!;
        document["sample"] = sample;
        if (manager) document["branches"]![0]!["managerIdentifier"] = "seed-manager";
        if (invalidPhone) document["branches"]![1]!["phone"] = "bad";
        string path = Path.Combine(Path.GetTempPath(), $"bep-seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, document.ToJsonString(), TestContext.Current.CancellationToken);
        return path;
    }
}
