using Bepthaymao.Application.Branches;
using Bepthaymao.Application.Branches.UpsertBranches;
using Bepthaymao.Domain.Branches;
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
        BranchUpsertItem item = new(Guid.CreateVersion7(), "Mẫu", "Địa chỉ", "0901234567", "06:00", "21:30", true);
        UpsertBranchesCommandHandler handler = new(repository, clock);
        Assert.Equal(new(1, 0, 0), await handler.Handle(new([item]), TestContext.Current.CancellationToken));
        repository.Received(1).Add(Arg.Is<Branch>(branch => branch.CreatedAt == now));
        Branch existing = Branch.Create(item.Id, item.Name, item.Address, item.Phone, new(6, 0), new(21, 30), true, now);
        repository.FindAsync(item.Id, Arg.Any<CancellationToken>()).Returns(existing);
        Assert.Equal(new(0, 0, 1), await handler.Handle(new([item]), TestContext.Current.CancellationToken));
        Assert.Equal(new(0, 1, 0), await handler.Handle(new([item with { Name = "Đổi tên" }]), TestContext.Current.CancellationToken));
        Assert.Equal(now, existing.CreatedAt);
    }

    [Fact]
    public async Task ValidatorRejectsEmptyDuplicateAndInvalidTime()
    {
        UpsertBranchesCommandValidator validator = new();
        Assert.False((await validator.ValidateAsync(new UpsertBranchesCommand([]), TestContext.Current.CancellationToken)).IsValid);
        BranchUpsertItem item = new(Guid.CreateVersion7(), "Mẫu", "Địa chỉ", "0901234567", "25:00", "21:30", true);
        var result = await validator.ValidateAsync(new UpsertBranchesCommand([item, item]), TestContext.Current.CancellationToken);
        Assert.Contains(result.Errors, error => error.PropertyName == "Branches[0].OpenTime");
        Assert.Contains(result.Errors, error => error.PropertyName == "Branches[1].Id");
        Assert.True((await validator.ValidateAsync(new UpsertBranchesCommand([item with { OpenTime = "06:00" }]), TestContext.Current.CancellationToken)).IsValid);
    }
}
