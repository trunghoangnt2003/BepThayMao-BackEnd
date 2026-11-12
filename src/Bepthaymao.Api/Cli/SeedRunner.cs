using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Branches.UpsertBranches;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Api.Cli;

public sealed record SeedOptions(string OwnerUserName, string OwnerName, string? OwnerPassword,
    string? BranchFile, string EnvironmentName, string? ManagerPassword);
public sealed record SeedResult(string Owner, int Inserted, int Updated, int Unchanged, int Managers, string? Note = null);
public sealed class SeedConfigurationException(string message) : Exception(message);

public sealed class SeedRunner(AppDbContext db, AccountSeeder accounts, ICommandHandler<UpsertBranchesCommand, UpsertBranchesResult> handler)
{
    public async Task<SeedResult> RunAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        if (options.OwnerPassword is null || options.OwnerPassword.Length < 10)
            throw new SeedConfigurationException("Thiếu SEED_OWNER_PASSWORD (tối thiểu 10 ký tự).");
        BranchSeedDocument? document = options.BranchFile is null ? null : await BranchSeedFile.ReadAsync(options.BranchFile, cancellationToken);
        bool skipSample = document is { IsSample: true } && !string.Equals(options.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<BranchUpsertItem> items = skipSample || document is null ? [] : document.Branches;
        foreach (BranchUpsertItem item in items.Where(item => item.ManagerIdentifier is not null))
        {
            bool managerExists = await db.Users.AnyAsync(user => user.BranchId == item.Id, cancellationToken);
            if (!managerExists && (options.ManagerPassword is null || options.ManagerPassword.Length < 10))
                throw new SeedConfigurationException("Thiếu SEED_MANAGER_PASSWORD (tối thiểu 10 ký tự).");
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        (Guid ownerId, bool created) = await accounts.EnsureOwnerAsync(options.OwnerUserName, options.OwnerName, options.OwnerPassword, cancellationToken);
        UpsertBranchesResult branches = items.Count == 0 ? new(0, 0, 0) : await handler.Handle(new(ownerId, items), cancellationToken);
        int managers = 0;
        foreach (BranchUpsertItem item in items.Where(item => item.ManagerIdentifier is not null))
            if (await accounts.EnsureManagerAsync(item.Id, item.ManagerIdentifier!, options.ManagerPassword!, cancellationToken)) managers++;
        await transaction.CommitAsync(cancellationToken);
        return new(created ? "created" : "unchanged", branches.Inserted, branches.Updated, branches.Unchanged, managers,
            skipSample ? "bỏ qua cơ sở mẫu ngoài môi trường Development" : null);
    }
}
