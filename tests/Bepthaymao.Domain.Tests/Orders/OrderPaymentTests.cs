using Bepthaymao.Domain.Common;
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

    [Fact]
    public void ExpiryRecordsExpiredKindNotFreeText()
    {
        Order order = NewOrder();
        Assert.True(order.Expire(_now.AddMinutes(15)));
        Assert.Equal(CancellationKind.Expired, order.CancellationKind);
        Assert.Null(order.CancelReason);
        Assert.Equal(_now.AddMinutes(15), order.CancelledAt);
    }

    [Fact]
    public void CustomerCancellationRecordsByCustomerKind()
    {
        Order order = NewOrder();
        order.CancelByCustomer(_now.AddMinutes(1));
        Assert.Equal(CancellationKind.ByCustomer, order.CancellationKind);
        Assert.Null(order.CancelReason);
    }

    [Fact]
    public void StaffCancellationOfPaidOrderKeepsPaymentFactsAndRecordsReason()
    {
        Order order = NewOrder();
        Assert.True(order.ReceivePayment(40000, _now.AddMinutes(1), _now.AddMinutes(1)));
        order.CancelByStaff("  Hết nguyên liệu  ", _now.AddMinutes(5));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationKind.ByStaff, order.CancellationKind);
        Assert.Equal("Hết nguyên liệu", order.CancelReason);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(_now.AddMinutes(1), order.PaidAt);
    }

    [Fact]
    public void StaffCancellationNeedsReasonAndCannotRepeat()
    {
        Order order = NewOrder();
        Assert.Throws<DomainRuleException>(() => order.CancelByStaff("   ", _now));
        order.CancelByStaff("Quán đóng cửa", _now);
        Assert.Throws<DomainRuleException>(() => order.CancelByStaff("Lần hai", _now.AddMinutes(1)));
    }

    [Fact]
    public void PaymentOnStaffCancelledOrderIsRecordedLateAndNeverRestores()
    {
        Order order = NewOrder();
        order.CancelByStaff("Quán đóng cửa", _now.AddMinutes(1));
        Assert.False(order.ReceivePayment(40000, _now.AddMinutes(2), _now.AddMinutes(2)));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.LatePaid, order.PaymentStatus);
        Assert.Equal(CancellationKind.ByStaff, order.CancellationKind);
        Assert.False(order.RestoredFromExpiry);
    }

    [Fact]
    public void RestoredOrderClearsCancellationAndKeepsTransactionTime()
    {
        Order order = NewOrder();
        order.Expire(_now.AddMinutes(15));
        Assert.True(order.ReceivePayment(40000, _now.AddMinutes(17), _now.AddMinutes(19)));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Null(order.CancellationKind);
        Assert.Null(order.CancelledAt);
        Assert.True(order.RestoredFromExpiry);
        Assert.Equal(_now.AddMinutes(17), order.PaidAt);
    }

    [Fact]
    public void LateMarkFollowsTransactionTimeAtTheExactDeadline()
    {
        Order atDeadline = NewOrder();
        Assert.True(atDeadline.ReceivePayment(40000, _now.AddMinutes(15), _now.AddMinutes(15)));
        Assert.Equal(PaymentStatus.LatePaid, atDeadline.PaymentStatus);
        Assert.Equal(OrderStatus.Paid, atDeadline.Status);
        Order beforeDeadline = NewOrder();
        Assert.True(beforeDeadline.ReceivePayment(40000, _now.AddMinutes(15).AddTicks(-1), _now.AddMinutes(20)));
        Assert.Equal(PaymentStatus.Paid, beforeDeadline.PaymentStatus);
        Assert.False(beforeDeadline.LatePayment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40000)]
    public void NonPositiveAmountNeverSettlesOrChangesState(long amount)
    {
        Order order = NewOrder();
        Assert.False(order.ReceivePayment(amount, _now, _now));
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Null(order.PaidAt);
        Assert.Equal(0, order.OverpaidAmount);
    }

    [Fact]
    public void ReplayKeepsTheFirstPaymentFacts()
    {
        Order order = NewOrder();
        Assert.True(order.ReceivePayment(45000, _now.AddMinutes(1), _now.AddMinutes(1)));
        Assert.False(order.ReceivePayment(90000, _now.AddMinutes(2), _now.AddMinutes(2)));
        Assert.Equal(_now.AddMinutes(1), order.PaidAt);
        Assert.Equal(5000, order.OverpaidAmount);
    }

    [Fact]
    public void OrderAlreadyInFulfilmentIsNeverMovedBackToPaid()
    {
        Order order = NewOrder();
        Assert.True(order.ReceivePayment(40000, _now.AddMinutes(1), _now.AddMinutes(1)));
        Assert.False(order.ReceivePayment(40000, _now.AddMinutes(2), _now.AddMinutes(2)));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
    }
}
