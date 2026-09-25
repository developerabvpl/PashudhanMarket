using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Orders.Domain;

namespace UPBazaar.UnitTests.Orders;

/// <summary>
/// A coupon's discount on an order: taken off what is paid and collected, refunded as paid, and
/// passed to Settlements only when the seller bears it.
/// </summary>
public sealed class OrderDiscountTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);
    private static readonly Guid SellerA = Guid.NewGuid();
    private static readonly Guid SellerB = Guid.NewGuid();

    [Fact]
    public void The_discount_comes_off_the_total_and_what_each_parcel_collects()
    {
        var order = Place(PaymentMethod.CashOnDelivery, "Platform");

        order.Subtotal.ShouldBe(300m);
        order.Discount.ShouldBe(30m);
        order.Total.ShouldBe(270m);
        Part(order, SellerA).AmountDue.ShouldBe(180m);
        Part(order, SellerB).AmountDue.ShouldBe(90m);
    }

    [Fact]
    public void A_paid_parcel_cancelled_refunds_what_was_paid_for_it()
    {
        var order = Place(PaymentMethod.Online, "Platform");
        order.ConfirmPayment(270m, "pay_1", Now).IsSuccess.ShouldBeTrue();
        order.ClearDomainEvents();

        order.CancelPart(Part(order, SellerB).PublicId, "Out of stock.", Now);

        order.DomainEvents.OfType<OrderPartCancelledDomainEvent>().Single().RefundDue.ShouldBe(90m);
        order.Total.ShouldBe(180m);
    }

    [Fact]
    public void A_returned_parcel_refunds_what_was_paid_for_it()
    {
        var order = Place(PaymentMethod.Online, "Platform");
        order.ConfirmPayment(270m, "pay_1", Now);
        var part = Part(order, SellerA).PublicId;
        order.AdvancePart(part, OrderPartStatus.Delivered, Now, Window);
        order.RequestReturn(part, ReturnReason.Damaged, null, null, Now);
        order.ApproveReturn(part, null, "seller", Now);
        order.ClearDomainEvents();

        order.CompleteReturn(part, Now);

        order.DomainEvents.OfType<OrderPartReturnedDomainEvent>().Single().RefundDue.ShouldBe(180m);
    }

    [Theory]
    [InlineData("Platform", 0)]
    [InlineData("Seller", 20)]
    public void Settlements_hear_of_the_discount_only_when_the_seller_bears_it(string fundedBy, decimal sellerDiscount)
    {
        var order = Place(PaymentMethod.CashOnDelivery, fundedBy);

        order.AdvancePart(Part(order, SellerA).PublicId, OrderPartStatus.Delivered, Now, Window);

        var delivered = order.DomainEvents.OfType<OrderPartDeliveredDomainEvent>().Single();
        delivered.Subtotal.ShouldBe(200m);
        delivered.SellerDiscount.ShouldBe(sellerDiscount);
    }

    [Fact]
    public void Free_delivery_lifts_the_delivery_charge_of_the_parcels_it_covers()
    {
        var whole = PlaceFreeDelivery(PaymentMethod.CashOnDelivery, "Platform", SellerA, SellerB);

        whole.ShippingFee.ShouldBe(30m);
        whole.DeliveryDiscount.ShouldBe(30m);
        whole.Total.ShouldBe(300m);
        Part(whole, SellerA).AmountDue.ShouldBe(200m);

        var partial = PlaceFreeDelivery(PaymentMethod.CashOnDelivery, "Seller", SellerB);

        partial.DeliveryDiscount.ShouldBe(10m);
        partial.Total.ShouldBe(320m);
        Part(partial, SellerA).AmountDue.ShouldBe(220m);
        Part(partial, SellerB).AmountDue.ShouldBe(100m);
    }

    [Theory]
    [InlineData("Platform", 20)]
    [InlineData("Seller", 0)]
    public void A_seller_earns_a_lifted_delivery_share_unless_they_paid_for_the_coupon(string fundedBy, decimal earned)
    {
        var order = PlaceFreeDelivery(PaymentMethod.CashOnDelivery, fundedBy, SellerA);

        order.AdvancePart(Part(order, SellerA).PublicId, OrderPartStatus.Delivered, Now, Window);

        order.DomainEvents.OfType<OrderPartDeliveredDomainEvent>().Single().DeliveryFee.ShouldBe(earned);
    }

    [Fact]
    public void A_delivery_share_never_moves_between_parcels_the_coupon_does_and_does_not_cover()
    {
        var order = PlaceFreeDelivery(PaymentMethod.Online, "Seller", SellerB);
        order.ConfirmPayment(320m, "pay_1", Now).IsSuccess.ShouldBeTrue();
        order.ClearDomainEvents();

        // A's 20 was paid and has no other charged parcel to go to, so it is refunded.
        order.CancelPart(Part(order, SellerA).PublicId, "Out of stock.", Now);

        order.DomainEvents.OfType<OrderPartCancelledDomainEvent>().Single().RefundDue.ShouldBe(220m);
        Part(order, SellerB).DeliveryFee.ShouldBe(10m);
        order.Total.ShouldBe(100m);
    }

    [Fact]
    public void A_parcel_that_travelled_free_refunds_only_its_goods()
    {
        var order = PlaceFreeDelivery(PaymentMethod.Online, "Platform", SellerB);
        order.ConfirmPayment(320m, "pay_1", Now).IsSuccess.ShouldBeTrue();
        order.ClearDomainEvents();

        order.CancelPart(Part(order, SellerB).PublicId, "Out of stock.", Now);

        order.DomainEvents.OfType<OrderPartCancelledDomainEvent>().Single().RefundDue.ShouldBe(100m);
        Part(order, SellerA).DeliveryFee.ShouldBe(20m);
        order.Total.ShouldBe(220m);
    }

    private static OrderPart Part(Order order, Guid seller) => order.Parts.Single(p => p.SellerId == seller);

    /// <summary>Seller A: 200 with 20 off. Seller B: 100 with 10 off. Free delivery.</summary>
    private static Order Place(PaymentMethod method, string fundedBy) =>
        Order.Place(
            OrderNumber.New(Now),
            Guid.NewGuid(),
            method,
            DeliveryAddress.Create("Asha Devi", "9876543210", "12 Gaushala Road", null, null, "Lucknow", null, "uttar pradesh", "226001"),
            "INR",
            [
                new(SellerA, Guid.NewGuid(), "A-1", "Gobar Diya", 100m, 2, Discount: 20m),
                new(SellerB, Guid.NewGuid(), "B-1", "Dhoop Batti", 50m, 2, Discount: 10m),
            ],
            deliveryFee: 0m,
            ("WELCOME", fundedBy),
            Guid.NewGuid(),
            Now);

    /// <summary>
    /// Seller A: 200. Seller B: 100. A delivery charge of 30, shared 20 and 10, with a free-delivery
    /// coupon covering <paramref name="free"/>.
    /// </summary>
    private static Order PlaceFreeDelivery(PaymentMethod method, string fundedBy, params Guid[] free) =>
        Order.Place(
            OrderNumber.New(Now),
            Guid.NewGuid(),
            method,
            DeliveryAddress.Create("Asha Devi", "9876543210", "12 Gaushala Road", null, null, "Lucknow", null, "uttar pradesh", "226001"),
            "INR",
            [
                new(SellerA, Guid.NewGuid(), "A-1", "Gobar Diya", 100m, 2),
                new(SellerB, Guid.NewGuid(), "B-1", "Dhoop Batti", 50m, 2),
            ],
            deliveryFee: 30m,
            ("SHIPFREE", fundedBy),
            Guid.NewGuid(),
            Now,
            free);
}
