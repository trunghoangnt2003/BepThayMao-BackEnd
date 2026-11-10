using System.Data.Common;
using System.Text.Json;
using Bepthaymao.Application;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches.UpsertBranches;
using Bepthaymao.Domain.Common;
using Bepthaymao.Infrastructure;
using Bepthaymao.Infrastructure.Observability;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Bepthaymao.Api.Cli;

/// <summary><c>db migrate</c> and <c>db seed [--file path]</c>. Exit codes: 0 ok, 1 data/database error, 2 usage or missing file.</summary>
internal static class DbCommand
{
    public const string SeedFileVariable = "SEED_BRANCHES_FILE";

    public static Task<int> RunAsync(string[] args) => RunAsync(args, Console.Out, Console.Error, configure: null);

    internal static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        Action<IConfigurationBuilder>? configure)
    {
        if (args is not (["migrate"] or ["seed"] or ["seed", "--file", _]))
        {
            await error.WriteLineAsync("Cách dùng: db migrate | db seed [--file <đường dẫn>]").ConfigureAwait(false);
            return 2;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        configure?.Invoke(builder.Configuration);
        builder.Services.AddSerilog((services, logger) =>
            logger.ConfigureBepthaymao(builder.Configuration, "bepthaymao-cli").ReadFrom.Services(services));
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure();
        using IHost host = builder.Build();
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        try
        {
            if (args[0] == "migrate")
            {
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync().ConfigureAwait(false);
                await output.WriteLineAsync("Đã áp dụng migration.").ConfigureAwait(false);
                return 0;
            }

            string path = args.Length == 3 ? args[2]
                : Environment.GetEnvironmentVariable(SeedFileVariable) ?? Path.Combine("seed", "branches.sample.json");
            if (!File.Exists(path))
            {
                await error.WriteLineAsync($"Không tìm thấy tệp seed: {path}").ConfigureAwait(false);
                return 2;
            }

            IReadOnlyList<BranchUpsertItem> branches = await BranchSeedFile.ReadAsync(path, CancellationToken.None)
                .ConfigureAwait(false);
            UpsertBranchesResult result = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<UpsertBranchesCommand, UpsertBranchesResult>>()
                .Handle(new UpsertBranchesCommand(branches), CancellationToken.None).ConfigureAwait(false);
            await output.WriteLineAsync($"inserted={result.Inserted} updated={result.Updated} unchanged={result.Unchanged}")
                .ConfigureAwait(false);
            return 0;
        }
        catch (ValidationException exception)
        {
            string details = string.Join("; ", exception.Errors.Select(pair => $"{pair.Key}: {string.Join(", ", pair.Value)}"));
            await error.WriteLineAsync($"Dữ liệu seed không hợp lệ: {details}").ConfigureAwait(false);
            return 1;
        }
        catch (DomainRuleException exception)
        {
            await error.WriteLineAsync($"Dữ liệu seed không hợp lệ: {exception.Rule}: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            await error.WriteLineAsync($"Dữ liệu seed không hợp lệ: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException)
        {
            // Never echo the exception message: provider errors can embed connection details.
            await error.WriteLineAsync($"Lỗi cơ sở dữ liệu ({exception.GetType().Name}); kiểm tra ConnectionStrings:Default và PostgreSQL.")
                .ConfigureAwait(false);
            return 1;
        }
    }
}
