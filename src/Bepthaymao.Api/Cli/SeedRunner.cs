using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches.UpsertBranches;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Infrastructure.Seeding;

namespace Bepthaymao.Api.Cli;

public sealed record SeedOptions(string OwnerUserName, string OwnerName, string? OwnerPassword,
    string? BranchFile, string EnvironmentName, string? ManagerPassword);
public sealed record SeedResult(string Owner, int Inserted, int Updated, int Unchanged, int Managers);

public sealed class SeedRunner(AppDbContext db, AccountSeeder accounts, ICommandHandler<UpsertBranchesCommand, UpsertBranchesResult> handler)
{
    public async Task<SeedResult> RunAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        BranchSeedDocument? document = options.BranchFile is null ? null : await BranchSeedFile.ReadAsync(options.BranchFile, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        (Guid ownerId, bool created) = await accounts.EnsureOwnerAsync(options.OwnerUserName, options.OwnerName, options.OwnerPassword!, cancellationToken);
        UpsertBranchesResult branches = document is null ? new(0, 0, 0) : await handler.Handle(new(ownerId, document.Branches), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(created ? "created" : "unchanged", branches.Inserted, branches.Updated, branches.Unchanged, 0);
    }
}
