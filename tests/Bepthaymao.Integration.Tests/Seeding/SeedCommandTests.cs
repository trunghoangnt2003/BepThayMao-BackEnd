using Bepthaymao.Api.Cli;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Bepthaymao.Integration.Tests.Seeding;

[Collection(InfrastructureGroup.Name)]
public sealed class SeedCommandTests(InfrastructureFixture infrastructure)
{
    private static string SampleFile => Path.Combine(RepositoryPaths.Root, "seed", "branches.sample.json");

    [Fact]
    public async Task MigrateThenSeedTwiceIsIdempotent()
    {
        string database = await infrastructure.CreateDatabaseAsync(migrate: false);

        (int migrate, _, _) = await RunAsync(database, "migrate");
        (int first, string firstOutput, _) = await RunAsync(database, "seed", "--file", SampleFile);
        (int second, string secondOutput, _) = await RunAsync(database, "seed", "--file", SampleFile);

        Assert.Equal(0, migrate);
        Assert.Equal(0, first);
        Assert.Equal(0, second);
        Assert.Contains("inserted=3 updated=0 unchanged=0", firstOutput, StringComparison.Ordinal);
        Assert.Contains("inserted=0 updated=0 unchanged=3", secondOutput, StringComparison.Ordinal);
        await using AppDbContext context = InfrastructureFixture.CreateContext(database);
        Assert.Equal(3, await context.Branches.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidSeedFileWritesNothing()
    {
        string database = await infrastructure.CreateDatabaseAsync();
        string file = Path.Combine(Path.GetTempPath(), $"seed-{Guid.NewGuid():N}.json");
        string sample = await File.ReadAllTextAsync(SampleFile, TestContext.Current.CancellationToken);
        // Third branch gets an invalid phone: the whole batch must be rejected.
        await File.WriteAllTextAsync(file, sample.Replace("02800000003", "123", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        try
        {
            (int exitCode, _, string error) = await RunAsync(database, "seed", "--file", file);

            Assert.Equal(1, exitCode);
            Assert.Contains("phone", error, StringComparison.Ordinal);
            await using AppDbContext context = InfrastructureFixture.CreateContext(database);
            Assert.Equal(0, await context.Branches.CountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("seed", "--file")]
    public async Task WrongUsageExitsWith2(params string[] args)
    {
        (int exitCode, _, string error) = await RunAsync(ApiFactory.ClosedPostgres, args);
        Assert.Equal(2, exitCode);
        Assert.Contains("Cách dùng", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSeedFileExitsWith2()
    {
        (int exitCode, _, string error) = await RunAsync(ApiFactory.ClosedPostgres, "seed", "--file", "khong-co.json");
        Assert.Equal(2, exitCode);
        Assert.Contains("khong-co.json", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseErrorDoesNotPrintConnectionString()
    {
        (int exitCode, _, string error) = await RunAsync(ApiFactory.ClosedPostgres, "migrate");
        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("Host=", error, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string connectionString, params string[] args)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        int exitCode = await DbCommand.RunAsync(args, output, error, configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Serilog:MinimumLevel:Default"] = "Warning",
            }));
        return (exitCode, output.ToString(), error.ToString());
    }
}
