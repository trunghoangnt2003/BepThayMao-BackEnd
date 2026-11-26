using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Orders;
using Bepthaymao.Domain.Orders;
using NSubstitute;

namespace Bepthaymao.Application.Tests.Orders;

public sealed class AuthorizeOrderWatchTests
{
    private static Order SampleOrder()
    {
        DateTimeOffset now = new(2026, 11, 26, 2, 0, 0, TimeSpan.Zero);
        return Order.Place(
            Guid.CreateVersion7(),
            "BTM-TEST01",
            Guid.CreateVersion7(),
            new("Nguyễn An", "0901234567", "12 Nguyễn Huệ", 10.77, 106.7, 1.0, null),
            [OrderItem.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Xôi xéo", 35000, 1)],
            isNextDay: false,
            deliveryDate: DateOnly.FromDateTime(now.UtcDateTime),
            now);
    }

    [Fact]
    public async Task AllowsValidPhoneMatch()
    {
        Order order = SampleOrder();
        IOrderStore store = Substitute.For<IOrderStore>();
        IOrderAccessGrants grants = Substitute.For<IOrderAccessGrants>();
        store.FindByCodeAsync("BTM-TEST01", Arg.Any<CancellationToken>()).Returns(order);

        AuthorizeOrderWatchQueryHandler handler = new(store, grants);
        Guid id = await handler.Handle(new("BTM-TEST01", "0901234567", []), CancellationToken.None);

        Assert.Equal(order.Id, id);
    }

    [Fact]
    public async Task AllowsValidAccessTokenWithoutPhone()
    {
        Order order = SampleOrder();
        IOrderStore store = Substitute.For<IOrderStore>();
        IOrderAccessGrants grants = Substitute.For<IOrderAccessGrants>();
        store.FindByCodeAsync("BTM-TEST01", Arg.Any<CancellationToken>()).Returns(order);
        grants.AllowsAsync(order.Id, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(true);

        AuthorizeOrderWatchQueryHandler handler = new(store, grants);
        Guid id = await handler.Handle(new("BTM-TEST01", null, ["valid-token"]), CancellationToken.None);

        Assert.Equal(order.Id, id);
    }

    [Fact]
    public async Task RejectsWrongPhoneAndMissingTokenWithNotFound()
    {
        Order order = SampleOrder();
        IOrderStore store = Substitute.For<IOrderStore>();
        IOrderAccessGrants grants = Substitute.For<IOrderAccessGrants>();
        store.FindByCodeAsync("BTM-TEST01", Arg.Any<CancellationToken>()).Returns(order);
        grants.AllowsAsync(order.Id, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(false);

        AuthorizeOrderWatchQueryHandler handler = new(store, grants);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new("BTM-TEST01", "0999999999", []), CancellationToken.None));
    }
}
