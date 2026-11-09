using Bepthaymao.Application;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches.UpsertBranches;
using Bepthaymao.Infrastructure;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Api.Cli;

internal static class DbCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("migrate" or "seed") ||
            (args.Length != 1 && !(args.Length == 3 && args[0] == "seed" && args[1] == "--file")))
        {
            Console.Error.WriteLine("Usage: db migrate | db seed [--file <path>]");
            return 2;
        }
        string path = args.Length == 3 ? args[2] : Environment.GetEnvironmentVariable("SEED_BRANCHES_FILE") ?? "seed/branches.sample.json";
        if (args[0] == "seed" && !File.Exists(path))
        {
            Console.Error.WriteLine($"Không thấy tệp: {path}");
            return 2;
        }
        try
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
            using IHost host = builder.Build();
            using IServiceScope scope = host.Services.CreateScope();
            if (args[0] == "migrate")
            {
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
                Console.WriteLine("Database migrated.");
            }
            else
            {
                var items = await BranchSeedFile.ReadAsync(path, CancellationToken.None);
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpsertBranchesCommand, UpsertBranchesResult>>();
                UpsertBranchesResult result = await handler.Handle(new(items), CancellationToken.None);
                Console.WriteLine(FormattableString.Invariant($"inserted={result.Inserted} updated={result.Updated} unchanged={result.Unchanged}"));
            }
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine("Không thể cập nhật dữ liệu. Kiểm tra tệp đầu vào và kết nối cơ sở dữ liệu.");
            return 1;
        }
    }
}
