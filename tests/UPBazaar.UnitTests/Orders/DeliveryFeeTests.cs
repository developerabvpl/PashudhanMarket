using UPBazaar.Modules.Orders;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Orders.Domain;

namespace UPBazaar.UnitTests.Orders;

/// <summary>
/// The delivery charge: shared across sellers' parcels by the value of their goods, moved to
/// parcels not yet packed when one is cancelled, and given back only when nothing can carry it.
/// </summary>
public sealed class DeliveryFeeTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);
    private static readonly Guid SellerA = Guid.NewGuid();
    private static readonly Guid SellerB = Guid.NewGuid();

    [Fact]
    public void The_charge_is_shared_by_value_and_the_shares_add_up_exactly()
    {
        // Seller A's goods are 250, B's 60: 49 x 250/310 = 39.516.., 49 x 60/310 = 9.483..
        var order = Place(PaymentMethod.CashOnDelivery, 49m);

        Share(order, SellerA).ShouldBe(39.52m);
        Share(order, SellerB).ShouldBe(9.48m);
        order.ShippingFee.ShouldBe(49m);
        order.Total.ShouldBe(359m);
        order.Parts.Single(p => p.SellerId == SellerA).AmountDue.ShouldBe(289.52m);
    }

    [Fact]
    public void A_cancelled_parcel_s_share_moves_to_one_not_yet_packed()
    {
        var order = Place(PaymentMethod.CashOnDelivery, 49m);

        order.CancelPart(Part(order, SellerB), "Out of stock.", Now).IsSuccess.ShouldBeTrue();

        Share(order, SellerA).ShouldBe(49m);
        Share(order, SellerB).ShouldBe(0m);
        order.ShippingFee.ShouldBe(49m);
    }

    [Fact]
    public void With_only_packed_parcels_left_the_share_is_given_back()
    {
        var order = Place(PaymentMethod.Online, 49m);
        order.ConfirmPayment(359m, "pay_1", Now).IsSuccess.ShouldBeTrue();
        order.AdvancePart(Part(order, SellerA), OrderPartStatus.Packed, Now, Window).IsSuccess.ShouldBeTrue();
        order.ClearDomainEvents();

        order.CancelPart(Part(order, SellerB), "Out of stock.", Now).IsSuccess.ShouldBeTrue();

        // The packed parcel is booked at a fixed amount, so it keeps its own share only.
        Share(order, SellerA).ShouldBe(39.52m);
        order.ShippingFee.ShouldBe(39.52m);
        order.DomainEvents.OfType<OrderPartCancelledDomainEvent>().Single().RefundDue.ShouldBe(60m + 9.48m);
    }

    [Fact]
    public void Cancelling_the_whole_paid_order_refunds_everything_including_delivery()
    {
        var order = Place(PaymentMethod.Online, 49m);
        order.ConfirmPayment(359m, "pay_1", Now).IsSuccess.ShouldBeTrue();
        order.ClearDomainEvents();

        order.Cancel("Changed my mind.", Now).IsSuccess.ShouldBeTrue();

        order.DomainEvents.OfType<OrderPartCancelledDomainEvent>().Sum(e => e.RefundDue).ShouldBe(359m);
        order.ShippingFee.ShouldBe(0m);

        // The total counts only what is kept, which is nothing; what was paid and is coming back
        // is what tells the buyer where their money is.
        order.Total.ShouldBe(0m);
        order.AmountPaid.ShouldBe(359m);
        order.RefundTotal.ShouldBe(359m);
        order.DomainEvents.OfType<OrderPartCancelledDomainEvent>().ShouldAllBe(e => e.OrderCancelled);
    }

    [Fact]
    public void A_part_cancelled_on_its_own_counts_the_delivery_it_gave_back_in_the_refund()
    {
        var order = Place(PaymentMethod.Online, 49m);
        order.ConfirmPayment(359m, "pay_1", Now).IsSuccess.ShouldBeTrue();
        order.AdvancePart(Part(order, SellerA), OrderPartStatus.Packed, Now, Window).IsSuccess.ShouldBeTrue();
        order.ClearDomainEvents();

        order.CancelPart(Part(order, SellerB), "Out of stock.", Now).IsSuccess.ShouldBeTrue();

        order.AmountPaid.ShouldBe(359m);
        order.RefundTotal.ShouldBe(60m + 9.48m);
        order.DomainEvents.OfType<OrderPartCancelledDomainEvent>().Single().OrderCancelled.ShouldBeFalse();
    }

    [Fact]
    public void Cash_on_delivery_has_nothing_paid_and_nothing_to_refund()
    {
        var order = Place(PaymentMethod.CashOnDelivery, 49m);

        order.Cancel("Changed my mind.", Now).IsSuccess.ShouldBeTrue();

        order.AmountPaid.ShouldBeNull();
        order.RefundTotal.ShouldBe(0m);
    }

    [Fact]
    public void Payment_must_cover_the_delivery_charge()
    {
        var order = Place(PaymentMethod.Online, 49m);

        order.ConfirmPayment(310m, "pay_1", Now).Error.ShouldBe(OrderErrors.AmountMismatch);
    }

    [Fact]
    public void The_seller_is_told_their_delivery_share_on_delivery()
    {
        var order = Place(PaymentMethod.CashOnDelivery, 49m);

        order.AdvancePart(Part(order, SellerB), OrderPartStatus.Delivered, Now, Window).IsSuccess.ShouldBeTrue();

        var delivered = order.DomainEvents.OfType<OrderPartDeliveredDomainEvent>().Single();
        delivered.Subtotal.ShouldBe(60m);
        delivered.DeliveryFee.ShouldBe(9.48m);
    }

    [Theory]
    [InlineData(498.99, 49)]
    [InlineData(499, 0)]
    [InlineData(2000, 0)]
    public void Orders_from_the_threshold_up_are_delivered_free(decimal subtotal, decimal fee) =>
        new OrdersModuleOptions().DeliveryFeeFor(subtotal).ShouldBe(fee);

    [Fact]
    public void A_zero_threshold_charges_every_order() =>
        new OrdersModuleOptions { FreeDeliveryFrom = 0m }.DeliveryFeeFor(5000m).ShouldBe(49m);

    private static decimal Share(Order order, Guid seller) => order.Parts.Single(p => p.SellerId == seller).DeliveryFee;

    private static Guid Part(Order order, Guid seller) => order.Parts.Single(p => p.SellerId == seller).PublicId;

    private static Order Place(PaymentMethod method, decimal deliveryFee) =>
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
            deliveryFee,
            coupon: null,
            Guid.NewGuid(),
            Now);
}
