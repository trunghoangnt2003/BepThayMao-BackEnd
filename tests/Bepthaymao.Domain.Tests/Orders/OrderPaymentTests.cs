using Bepthaymao.Domain.Orders;

namespace Bepthaymao.Domain.Tests.Orders;

public sealed class OrderPaymentTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 23, 3, 0, 0, TimeSpan.Zero);
    private static Order NewOrder() => Order.Place(Guid.NewGuid(), "BTMABC123", Guid.NewGuid(),
        new("Test", "0900000000", "Test", null, null, null, null),
        [OrderItem.Create(Guid.NewGuid(), Guid.NewGuid(), "Xôi", 40000, 1)], false, new(2026, 11, 23), _now);

    [Fact]
    public void PaymentMovesOrderToPaidOnlyOnce()
    {
        Order order = NewOrder();
        Assert.True(order.ReceivePayment(40000, _now.AddMinutes(1), _now.AddMinutes(1)));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.False(order.ReceivePayment(40000, _now.AddMinutes(2), _now.AddMinutes(2)));
    }

    [Fact]
    public void ExpiredOrderIsRestoredByLatePayment()
    {
        Order order = NewOrder();
        Assert.False(order.Expire(_now.AddMinutes(14)));
        Assert.True(order.Expire(_now.AddMinutes(15)));
        Assert.True(order.ReceivePayment(41000, _now.AddMinutes(17), _now.AddMinutes(18)));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.LatePaid, order.PaymentStatus);
    }

    [Fact]
    public void CustomerCancellationCannotBeRestored()
    {
        Order order = NewOrder();
        order.CancelByCustomer(_now.AddMinutes(1));
        Assert.False(order.ReceivePayment(40000, _now.AddMinutes(2), _now.AddMinutes(2)));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void DelayedWebhookForOnTimePaymentIsNotLate()
    {
        Order order = NewOrder();
        order.Expire(_now.AddMinutes(15));
        Assert.True(order.ReceivePayment(40000, _now.AddMinutes(14), _now.AddMinutes(20)));
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public void InsufficientPaymentDoesNotSettle()
    {
        Order order = NewOrder();
        Assert.False(order.ReceivePayment(39999, _now, _now));
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
    }
}
