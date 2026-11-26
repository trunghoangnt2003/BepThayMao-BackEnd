using Bepthaymao.Domain.Common;
using Bepthaymao.Domain.Orders;

namespace Bepthaymao.Domain.Tests.Orders;

public sealed class OrderFulfilmentTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero);

    private static Order NewOrder() => Order.Place(Guid.NewGuid(), "BTMFUL001", Guid.NewGuid(),
        new("Test", "0900000000", "Test", null, null, null, null),
        [OrderItem.Create(Guid.NewGuid(), Guid.NewGuid(), "Xôi", 40000, 1)], false, new(2026, 11, 25), _now);

    private static Order NewPaidOrder()
    {
        Order order = NewOrder();
        Assert.True(order.ReceivePayment(40000, _now.AddMinutes(1), _now.AddMinutes(2)));
        return order;
    }

    [Fact]
    public void FulfilmentChainAdvancesSequentiallyAndRecordsTimestamps()
    {
        Order order = NewPaidOrder();
        Assert.Equal(_now.AddMinutes(2), order.PaidRecordedAt);

        order.Accept(_now.AddMinutes(3));
        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Equal(_now.AddMinutes(3), order.AcceptedAt);

        order.StartPreparing(_now.AddMinutes(4));
        Assert.Equal(OrderStatus.Preparing, order.Status);

        order.StartDelivering(_now.AddMinutes(5));
        Assert.Equal(OrderStatus.Delivering, order.Status);

        order.Complete(_now.AddMinutes(6));
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(_now.AddMinutes(6), order.CompletedAt);
    }

    [Fact]
    public void SkippingOrRepeatingOrReversingStepsThrowsDomainRuleException()
    {
        Order unpaid = NewOrder();
        Assert.Throws<DomainRuleException>(() => unpaid.Accept(_now));

        Order paid = NewPaidOrder();
        Assert.Throws<DomainRuleException>(() => paid.StartPreparing(_now));
        Assert.Throws<DomainRuleException>(() => paid.StartDelivering(_now));
        Assert.Throws<DomainRuleException>(() => paid.Complete(_now));

        paid.Accept(_now.AddMinutes(3));
        Assert.Throws<DomainRuleException>(() => paid.Accept(_now.AddMinutes(4)));
        Assert.Throws<DomainRuleException>(() => paid.StartDelivering(_now.AddMinutes(4)));

        paid.StartPreparing(_now.AddMinutes(4));
        Assert.Throws<DomainRuleException>(() => paid.Accept(_now.AddMinutes(5)));
        Assert.Throws<DomainRuleException>(() => paid.StartPreparing(_now.AddMinutes(5)));

        paid.StartDelivering(_now.AddMinutes(5));
        paid.Complete(_now.AddMinutes(6));
        Assert.Throws<DomainRuleException>(() => paid.Accept(_now.AddMinutes(7)));
        Assert.Throws<DomainRuleException>(() => paid.StartPreparing(_now.AddMinutes(7)));
        Assert.Throws<DomainRuleException>(() => paid.StartDelivering(_now.AddMinutes(7)));
        Assert.Throws<DomainRuleException>(() => paid.Complete(_now.AddMinutes(7)));
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Accepted)]
    public void CancelByStaffAllowsEarlyStatusesAndUsesDefaultLabelWhenNoteIsBlank(OrderStatus initial)
    {
        Order order = NewOrder();
        if (initial is OrderStatus.Paid or OrderStatus.Accepted)
            Assert.True(order.ReceivePayment(40000, _now.AddMinutes(1), _now.AddMinutes(1)));
        if (initial == OrderStatus.Accepted)
            order.Accept(_now.AddMinutes(2));

        order.CancelByStaff(CancelReasonCode.OutOfDeliveryArea, null, _now.AddMinutes(3));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationKind.ByStaff, order.CancellationKind);
        Assert.Equal(CancelReasonCode.OutOfDeliveryArea, order.CancelReasonCode);
        Assert.Equal("Ngoài vùng giao", order.CancelReason);
    }

    [Fact]
    public void CancelByStaffRejectsPreparingDeliveringCompletedCancelledAndInvalidOther()
    {
        Order order = NewPaidOrder();
        order.Accept(_now.AddMinutes(3));
        order.StartPreparing(_now.AddMinutes(4));
        Assert.Throws<DomainRuleException>(() => order.CancelByStaff(CancelReasonCode.CustomerRequested, null, _now.AddMinutes(5)));

        order.StartDelivering(_now.AddMinutes(5));
        Assert.Throws<DomainRuleException>(() => order.CancelByStaff(CancelReasonCode.CustomerRequested, null, _now.AddMinutes(6)));

        order.Complete(_now.AddMinutes(6));
        Assert.Throws<DomainRuleException>(() => order.CancelByStaff(CancelReasonCode.CustomerRequested, null, _now.AddMinutes(7)));

        Order other = NewPaidOrder();
        Assert.Throws<DomainRuleException>(() => other.CancelByStaff(CancelReasonCode.Other, "", _now.AddMinutes(3)));
        Assert.Throws<DomainRuleException>(() => other.CancelByStaff(CancelReasonCode.Other, new string('a', Order.CancelReasonMaxLength + 1), _now.AddMinutes(3)));
    }
}
