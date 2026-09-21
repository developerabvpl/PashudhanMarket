using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Orders.Domain;

namespace UPBazaar.UnitTests.Orders;

public sealed class OrderTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SellerA = Guid.NewGuid();
    private static readonly Guid SellerB = Guid.NewGuid();

    [Fact]
    public void Lines_are_split_into_one_part_per_seller()
    {
        var order = Place(PaymentMethod.CashOnDelivery);

        order.Parts.Count.ShouldBe(2);
        order.Parts.Single(p => p.SellerId == SellerA).Lines.Count.ShouldBe(2);
        order.Subtotal.ShouldBe(2 * 100m + 50m + 3 * 20m);
        order.Total.ShouldBe(order.Subtotal);
    }

    [Fact]
    public void Cash_on_delivery_is_confirmed_at_once()
    {
        var order = Place(PaymentMethod.CashOnDelivery);

        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.PaymentStatus.ShouldBe(PaymentStatus.CashOnDelivery);
        order.PaymentDueAtUtc.ShouldBeNull();
        order.Parts.ShouldAllBe(p => p.Status == OrderPartStatus.Confirmed);
        order.DomainEvents.OfType<OrderConfirmedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_online_order_waits_for_payment_with_a_deadline()
    {
        var order = Place(PaymentMethod.Online);

        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.PaymentDueAtUtc.ShouldBe(Now + Order.PaymentWindow);
        order.Parts.ShouldAllBe(p => p.Status == OrderPartStatus.AwaitingPayment);
        order.DomainEvents.OfType<OrderConfirmedDomainEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void Paying_the_exact_amount_in_time_confirms_the_order()
    {
        var order = Place(PaymentMethod.Online);

        order.ConfirmPayment(order.Total, "pay_1", Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        order.PaymentReference.ShouldBe("pay_1");
        order.Parts.ShouldAllBe(p => p.Status == OrderPartStatus.Confirmed);
    }

    [Fact]
    public void The_same_payment_reported_twice_is_harmless_but_a_second_payment_is_refused()
    {
        var order = Place(PaymentMethod.Online);
        order.ConfirmPayment(order.Total, "pay_1", Now).IsSuccess.ShouldBeTrue();

        order.ConfirmPayment(order.Total, "pay_1", Now).IsSuccess.ShouldBeTrue();
        order.ConfirmPayment(order.Total, "pay_2", Now).Error.ShouldBe(OrderErrors.AlreadyPaidElsewhere);
    }

    [Fact]
    public void A_wrong_amount_or_a_late_payment_is_refused()
    {
        var order = Place(PaymentMethod.Online);

        order.ConfirmPayment(order.Total - 1m, "pay_1", Now).Error.ShouldBe(OrderErrors.AmountMismatch);
        order.ConfirmPayment(order.Total, "pay_1", Now + Order.PaymentWindow + TimeSpan.FromSeconds(1)).Error
            .ShouldBe(OrderErrors.PaymentTooLate);
        order.Status.ShouldBe(OrderStatus.PendingPayment);
    }

    [Fact]
    public void Cancelling_cancels_every_part_and_owes_no_refund_before_payment()
    {
        var order = Place(PaymentMethod.Online);

        var cancelled = order.Cancel("Changed my mind", Now);

        cancelled.Value.Count.ShouldBe(2);
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.DomainEvents.OfType<OrderPartCancelledDomainEvent>().ShouldAllBe(e => e.RefundDue == 0m);
        order.DomainEvents.OfType<OrderCancelledDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Cancelling_a_paid_order_owes_each_part_back()
    {
        var order = Place(PaymentMethod.Online);
        order.ConfirmPayment(order.Total, "pay_1", Now);

        order.Cancel("Changed my mind", Now);

        order.DomainEvents.OfType<OrderPartCancelledDomainEvent>().Sum(e => e.RefundDue).ShouldBe(310m);
    }

    [Fact]
    public void Nothing_can_be_cancelled_once_a_part_has_shipped()
    {
        var order = Place(PaymentMethod.CashOnDelivery);
        order.AdvancePart(order.Parts.First().PublicId, OrderPartStatus.Shipped);

        order.CanCancel.ShouldBeFalse();
        order.Cancel("Too late", Now).Error.ShouldBe(OrderErrors.CannotCancel);
    }

    [Fact]
    public void Cancelling_one_part_leaves_the_rest_and_drops_it_from_the_total()
    {
        var order = Place(PaymentMethod.CashOnDelivery);
        var partB = order.Parts.Single(p => p.SellerId == SellerB);

        order.CancelPart(partB.PublicId, "Seller out of stock", Now).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.Total.ShouldBe(250m);
    }

    [Fact]
    public void Cancelling_the_last_live_part_cancels_the_order()
    {
        var order = Place(PaymentMethod.CashOnDelivery);

        foreach (var part in order.Parts.ToList())
        {
            order.CancelPart(part.PublicId, "Seller closed", Now);
        }

        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void A_part_of_an_unpaid_order_cannot_be_cancelled_on_its_own()
    {
        var order = Place(PaymentMethod.Online);

        order.CancelPart(order.Parts.First().PublicId, "No", Now).Error.ShouldBe(OrderErrors.PartAwaitingPayment);
    }

    [Fact]
    public void Parts_move_forward_only_and_the_order_completes_when_all_live_parts_are_delivered()
    {
        var order = Place(PaymentMethod.CashOnDelivery);
        var partA = order.Parts.Single(p => p.SellerId == SellerA).PublicId;
        var partB = order.Parts.Single(p => p.SellerId == SellerB).PublicId;

        order.AdvancePart(partA, OrderPartStatus.Packed).IsSuccess.ShouldBeTrue();
        order.AdvancePart(partA, OrderPartStatus.Packed).Error.ShouldBe(OrderErrors.InvalidTransition);
        order.AdvancePart(partA, OrderPartStatus.Delivered).IsSuccess.ShouldBeTrue();
        order.AdvancePart(partA, OrderPartStatus.Shipped).Error.ShouldBe(OrderErrors.InvalidTransition);

        order.Status.ShouldBe(OrderStatus.Confirmed);

        order.CancelPart(partB, "Seller out of stock", Now);

        order.Status.ShouldBe(OrderStatus.Completed);
    }

    [Fact]
    public void An_unpaid_part_cannot_be_advanced()
    {
        var order = Place(PaymentMethod.Online);

        order.AdvancePart(order.Parts.First().PublicId, OrderPartStatus.Packed).Error
            .ShouldBe(OrderErrors.InvalidTransition);
    }

    [Theory]
    [InlineData("uttar pradesh", "Uttar Pradesh")]
    [InlineData("  Delhi ", "Delhi")]
    [InlineData("Narnia", null)]
    public void States_are_matched_to_their_listed_spelling(string input, string? expected) =>
        IndianStates.Canonical(input).ShouldBe(expected);

    [Fact]
    public void Order_numbers_carry_the_date_and_avoid_confusable_characters()
    {
        var number = OrderNumber.New(Now);

        number.ShouldStartWith("UPB-260921-");
        number.Length.ShouldBeLessThanOrEqualTo(OrderNumber.MaxLength);
        number[11..].ShouldNotContain('0');
        number[11..].ShouldNotContain('O');
        number[11..].ShouldNotContain('I');
    }

    private static Order Place(PaymentMethod method) =>
        Order.Place(
            OrderNumber.New(Now),
            Guid.NewGuid(),
            method,
            DeliveryAddress.Create("Asha Devi", "9876543210", "12 Gaushala Road", null, null, "Lucknow", null, "uttar pradesh", "226001"),
            "INR",
            [
                new(SellerA, Guid.NewGuid(), "A-1", "Gobar Diya", 100m, 2),
                new(SellerB, Guid.NewGuid(), "B-1", "Dhoop Batti", 20m, 3),
                new(SellerA, Guid.NewGuid(), "A-2", "Panchgavya Sabun", 50m, 1),
            ],
            Guid.NewGuid(),
            Now);
}
