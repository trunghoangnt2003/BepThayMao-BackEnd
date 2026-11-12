using Bepthaymao.Application.Branches;
using Bepthaymao.Application.Branches.UpsertBranches;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Common;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Bepthaymao.Application.Tests.Branches;

public sealed class UpsertBranchesTests
{
    [Fact]
    public async Task HandlerInsertsUpdatesAndPreservesCreationTime()
    {
        IBranchRepository repository = Substitute.For<IBranchRepository>();
        DateTimeOffset now = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        FakeTimeProvider clock = new(now);
        Guid ownerId = Guid.CreateVersion7();
        BranchUpsertItem item = new(Guid.CreateVersion7(), "Mẫu", "Địa chỉ", "0901234567", "06:00", "21:30", true);
        UpsertBranchesCommandHandler handler = new(repository, clock);
        Assert.Equal(new(1, 0, 0), await handler.Handle(new(ownerId, [item]), TestContext.Current.CancellationToken));
        repository.Received(1).Add(Arg.Is<Branch>(branch => branch.CreatedAt == now));
        Branch existing = Branch.Create(item.Id, ownerId, new(item.Name, item.Address, item.Phone, 0, 0, new(6, 0), new(21, 30), 10, [], null, null, null, true), now);
        repository.FindAsync(item.Id, Arg.Any<CancellationToken>()).Returns(existing);
        Assert.Equal(new(0, 0, 1), await handler.Handle(new(ownerId, [item]), TestContext.Current.CancellationToken));
        Assert.Equal(new(0, 1, 0), await handler.Handle(new(ownerId, [item with { Name = "Đổi tên" }]), TestContext.Current.CancellationToken));
        Assert.Equal(now, existing.CreatedAt);
    }

    [Fact]
    public async Task ValidatorRejectsEmptyDuplicateAndInvalidTime()
    {
        UpsertBranchesCommandValidator validator = new();
        Guid ownerId = Guid.CreateVersion7();
        Assert.False((await validator.ValidateAsync(new UpsertBranchesCommand(ownerId, []), TestContext.Current.CancellationToken)).IsValid);
        BranchUpsertItem item = new(Guid.CreateVersion7(), "Mẫu", "Địa chỉ", "0901234567", "25:00", "21:30", true);
        var result = await validator.ValidateAsync(new UpsertBranchesCommand(ownerId, [item, item]), TestContext.Current.CancellationToken);
        Assert.Contains(result.Errors, error => error.PropertyName == "branches[0].openTime");
        Assert.Contains(result.Errors, error => error.PropertyName == "branches[1].id");
        Assert.True((await validator.ValidateAsync(new UpsertBranchesCommand(ownerId, [item with { OpenTime = "06:00" }]), TestContext.Current.CancellationToken)).IsValid);
    }

    [Fact]
    public async Task ValidatorReportsCoordinatePath()
    {
        Guid ownerId = Guid.CreateVersion7();
        BranchUpsertItem item = new(Guid.CreateVersion7(), "Mẫu", "Địa chỉ", "0901234567", "06:00", "21:30", true, Lat: 91);
        var validation = await new UpsertBranchesCommandValidator().ValidateAsync(new UpsertBranchesCommand(ownerId, [item]), TestContext.Current.CancellationToken);
        Assert.Contains(validation.Errors, error => error.PropertyName == "branches[0].lat");
    }

    [Fact]
    public async Task HandlerRejectsAnotherOwner()
    {
        Guid ownerId = Guid.CreateVersion7();
        BranchUpsertItem item = new(Guid.CreateVersion7(), "Mẫu", "Địa chỉ", "0901234567", "06:00", "21:30", true);
        IBranchRepository repository = Substitute.For<IBranchRepository>();
        Branch existing = Branch.Create(item.Id, Guid.CreateVersion7(), new("Original", "Address", "0901234567", 10, 106, new(6, 0), new(21, 30), 10, [], null, null, null, true), DateTimeOffset.UtcNow);
        repository.FindAsync(item.Id, Arg.Any<CancellationToken>()).Returns(existing);
        UpsertBranchesCommandHandler handler = new(repository, TimeProvider.System);
        DomainRuleException error = await Assert.ThrowsAsync<DomainRuleException>(() => handler.Handle(new(ownerId, [item with { Lat = 10 }]), TestContext.Current.CancellationToken));
        Assert.Equal("ownerId", error.Rule);
        Assert.Equal("Original", existing.Name);
    }
}
