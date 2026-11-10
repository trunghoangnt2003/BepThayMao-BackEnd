using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Branches;
using Bepthaymao.Application.Branches.UpsertBranches;
using Bepthaymao.Domain.Branches;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ValidationException = Bepthaymao.Application.Abstractions.Errors.ValidationException;

namespace Bepthaymao.Application.Tests.Branches;

public sealed class UpsertBranchesTests
{
    private static readonly BranchUpsertItem _first = new(Guid.CreateVersion7(), "Cơ sở Mẫu 1", "Số 1", "02800000001",
        new TimeOnly(6, 0), new TimeOnly(21, 30), true);
    private static readonly BranchUpsertItem _second = new(Guid.CreateVersion7(), "Cơ sở Mẫu 2", "Số 2", "02800000002",
        new TimeOnly(7, 0), new TimeOnly(21, 0), false);

    [Fact]
    public async Task RunningTwiceInsertsThenReportsUnchanged()
    {
        using ServiceProvider provider = CreateProvider(out FakeStore store);
        UpsertBranchesResult first = await HandleAsync(provider, [_first, _second]);
        UpsertBranchesResult second = await HandleAsync(provider, [_first, _second]);

        first.Should().Be(new UpsertBranchesResult(2, 0, 0));
        second.Should().Be(new UpsertBranchesResult(0, 0, 2));
        store.Branches.Should().HaveCount(2);
        store.Saves.Should().Be(2);
    }

    [Fact]
    public async Task ChangedItemIsCountedAsUpdated()
    {
        using ServiceProvider provider = CreateProvider(out FakeStore store);
        await HandleAsync(provider, [_first]);
        UpsertBranchesResult result = await HandleAsync(provider, [_first with { Name = "Cơ sở Đổi Tên" }]);

        result.Should().Be(new UpsertBranchesResult(0, 1, 0));
        store.Branches[_first.Id].Name.Should().Be("Cơ sở Đổi Tên");
    }

    [Fact]
    public async Task InvalidBatchIsRejectedBeforeAnyWrite()
    {
        using ServiceProvider provider = CreateProvider(out FakeStore store);
        BranchUpsertItem invalid = _second with { CloseTime = new TimeOnly(5, 0) };

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            HandleAsync(provider, [_first, invalid]));

        exception.Errors.Keys.Should().Contain(key => key.StartsWith("branches", StringComparison.Ordinal));
        store.Branches.Should().BeEmpty();
        store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task DuplicateIdsAreRejected()
    {
        using ServiceProvider provider = CreateProvider(out FakeStore store);
        await Assert.ThrowsAsync<ValidationException>(() => HandleAsync(provider, [_first, _first]));
        store.Saves.Should().Be(0);
    }

    private static async Task<UpsertBranchesResult> HandleAsync(ServiceProvider provider, BranchUpsertItem[] items)
    {
        using IServiceScope scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<UpsertBranchesCommand, UpsertBranchesResult>>()
            .Handle(new UpsertBranchesCommand(items), TestContext.Current.CancellationToken);
    }

    private static ServiceProvider CreateProvider(out FakeStore store)
    {
        FakeStore instance = new();
        store = instance;
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(instance);
        services.AddScoped<IBranchRepository>(provider => provider.GetRequiredService<FakeStore>());
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<FakeStore>());
        services.AddApplication();
        return services.BuildServiceProvider(validateScopes: true);
    }

    internal sealed class FakeStore : IBranchRepository, IUnitOfWork
    {
        public Dictionary<Guid, Branch> Branches { get; } = [];
        public int Saves { get; private set; }

        public Task<Branch?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Branches.GetValueOrDefault(id));

        public void Add(Branch branch) => Branches.Add(branch.Id, branch);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
