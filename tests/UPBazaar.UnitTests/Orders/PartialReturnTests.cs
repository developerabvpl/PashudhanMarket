using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Orders.Domain;

namespace UPBazaar.UnitTests.Orders;

/// <summary>
/// Returning some units of a parcel: the buyer is refunded what they paid for those units, keeps
/// the rest, and loses the coupon's discount if what they keep falls below its minimum order.
/// </summary>
public sealed class PartialReturnTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);
    private static readonly Guid SellerA = Guid.NewGuid();
    private static readonly Guid SellerB = Guid.NewGuid();
    private static readonly Guid Diya = Guid.NewGuid();
    private static readonly Guid Dhoop = Guid.NewGuid();
    private static readonly Guid Cake = Guid.NewGuid();

    [Fact]
    public void Returning_some_units_refunds_what_was_paid_for_them_and_keeps_the_rest()
    {
        var order = Delivered("Platform", minimum: null);
        var part = PartOf(order, SellerA);

        order.RequestReturn(part.PublicId, ReturnReason.Damaged, null, null, Now, new Dictionary<Guid, int> { [Diya] = 1 }).IsSuccess.ShouldBeTrue();
        order.ClearDomainEvents();
        order.ApproveReturn(part.PublicId, null, "seller", Now).IsSuccess.ShouldBeTrue();

        part.ReturnRequest!.RefundDue.ShouldBe(90m);
        order.Subtotal.ShouldBe(350m);
        order.Discount.ShouldBe(35m);
        order.DomainEvents.OfType<OrderPartReturnApprovedDomainEvent>().Single().KeptGross.ShouldBe(250m);

        order.ClearDomainEvents();
        order.CompleteReturn(part.PublicId, Now);

        order.DomainEvents.OfType<OrderPartReturnedDomainEvent>().Single().RefundDue.ShouldBe(90m);
        part.CameBack.ShouldHaveSingleItem().Quantity.ShouldBe(1);
    }

    [Fact]
    public void A_seller_who_bore_the_coupon_earns_on_the_discounted_price_of_what_is_kept()
    {
        var order = Delivered("Seller", minimum: null);
        var part = PartOf(order, SellerA);

        order.RequestReturn(part.PublicId, ReturnReason.Damaged, null, null, Now, new Dictionary<Guid, int> { [Diya] = 1 });
        order.ClearDomainEvents();
        order.ApproveReturn(part.PublicId, null, "seller", Now);

        order.DomainEvents.OfType<OrderPartReturnApprovedDomainEvent>().Single().KeptGross.ShouldBe(225m);
    }

    [Fact]
    public void Keeping_less_than_the_coupon_minimum_takes_the_kept_discount_out_of_the_refund()
    {
        var order = Delivered("Seller", minimum: 400m);
        var part = PartOf(order, SellerA);

        order.RequestReturn(part.PublicId, ReturnReason.NoLongerNeeded, null, null, Now, new Dictionary<Guid, int> { [Diya] = 1 });
        order.ClearDomainEvents();
        order.ApproveReturn(part.PublicId, null, "seller", Now);

        // Paid 90 for the returned diya pack; the 35 discount on everything kept - 20 + 5 here,
        // 10 on the other seller's parcel - comes back out of it.
        part.ReturnRequest!.RefundDue.ShouldBe(55m);
        order.Discount.ShouldBe(0m);
        order.DomainEvents.OfType<OrderPartReturnApprovedDomainEvent>().Single().KeptGross.ShouldBe(250m);

        // The other seller bore the discount on a delivered parcel, so it is theirs again.
        var revoked = order.DomainEvents.OfType<OrderPartDiscountRevokedDomainEvent>().ShouldHaveSingleItem();
        revoked.SellerId.ShouldBe(SellerB);
        revoked.Amount.ShouldBe(10m);
    }

    [Fact]
    public void Meeting_the_minimum_with_what_is_kept_leaves_the_discount_alone()
    {
        var order = Delivered("Platform", minimum: 300m);
        var part = PartOf(order, SellerA);

        order.RequestReturn(part.PublicId, ReturnReason.NoLongerNeeded, null, null, Now, new Dictionary<Guid, int> { [Diya] = 1 });
        order.ApproveReturn(part.PublicId, null, "seller", Now);

        part.ReturnRequest!.RefundDue.ShouldBe(90m);
        order.Discount.ShouldBe(35m);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    public void Only_units_that_were_delivered_can_be_returned(int quantity)
    {
        var order = Delivered("Platform", minimum: null);
        var part = PartOf(order, SellerA);

        order.RequestReturn(part.PublicId, ReturnReason.Damaged, null, null, Now, new Dictionary<Guid, int> { [Diya] = quantity })
            .Error.ShouldBe(OrderErrors.InvalidReturnItems);
        order.RequestReturn(part.PublicId, ReturnReason.Damaged, null, null, Now, new Dictionary<Guid, int> { [Cake] = 1 })
            .Error.ShouldBe(OrderErrors.InvalidReturnItems);
    }

    [Fact]
    public void Each_line_that_came_back_is_inspected_on_its_own()
    {
        var order = Delivered("Platform", minimum: null);
        var part = PartOf(order, SellerA);

        order.RequestReturn(part.PublicId, ReturnReason.Damaged, null, null, Now, new Dictionary<Guid, int> { [Diya] = 2, [Dhoop] = 1 });
        order.ApproveReturn(part.PublicId, null, "seller", Now);
        order.CompleteReturn(part.PublicId, Now);

        order.InspectReturn(part.PublicId, new Dictionary<Guid, ReturnCondition> { [Diya] = ReturnCondition.Good }, null, "seller", Now)
            .Error.ShouldBe(OrderErrors.ConditionForEveryLine);

        order.InspectReturn(
                part.PublicId,
                new Dictionary<Guid, ReturnCondition> { [Diya] = ReturnCondition.Good, [Dhoop] = ReturnCondition.Damaged },
                "One box crushed",
                "seller",
                Now)
            .IsSuccess.ShouldBeTrue();

        part.ReturnCondition.ShouldBe(ReturnCondition.Damaged);
        part.CameBack.Where(x => x.Line.ReturnCondition == ReturnCondition.Good).ShouldHaveSingleItem().Quantity.ShouldBe(2);
    }

    [Fact]
    public void Returning_goods_whose_discount_was_already_taken_back_refunds_what_was_paid_for_them()
    {
        var order = Delivered("Seller", minimum: 400m);
        var a = PartOf(order, SellerA);
        var b = PartOf(order, SellerB);
        order.RequestReturn(a.PublicId, ReturnReason.NoLongerNeeded, null, null, Now, new Dictionary<Guid, int> { [Diya] = 1 });
        order.ApproveReturn(a.PublicId, null, "seller", Now);

        // The cake's 10 off came out of the first refund, so the buyer paid 100 for it in all.
        order.RequestReturn(b.PublicId, ReturnReason.NoLongerNeeded, null, null, Now);
        order.ClearDomainEvents();
        order.ApproveReturn(b.PublicId, null, "seller", Now);

        b.ReturnRequest!.RefundDue.ShouldBe(100m);
        b.KeptDiscount.ShouldBe(0m);
        order.Discount.ShouldBe(0m);
        order.DomainEvents.OfType<OrderPartReturnApprovedDomainEvent>().Single().KeptGross.ShouldBe(0m);
    }

    [Fact]
    public void A_parcel_still_on_its_way_keeps_its_discount()
    {
        var order = Delivered("Platform", minimum: 400m, deliverB: false);
        var a = PartOf(order, SellerA);

        order.RequestReturn(a.PublicId, ReturnReason.NoLongerNeeded, null, null, Now, new Dictionary<Guid, int> { [Diya] = 1 });
        order.ApproveReturn(a.PublicId, null, "seller", Now);

        // Only seller A's kept 25 is taken; the cake, not yet delivered, keeps its 10.
        a.ReturnRequest!.RefundDue.ShouldBe(65m);
        PartOf(order, SellerB).KeptDiscount.ShouldBe(10m);
    }

    [Fact]
    public void No_more_is_taken_back_than_the_refund_can_cover()
    {
        var order = Order.Place(
            OrderNumber.New(Now),
            Guid.NewGuid(),
            PaymentMethod.Online,
            DeliveryAddress.Create("Asha Devi", "9876543210", "12 Gaushala Road", null, null, "Lucknow", null, "uttar pradesh", "226001"),
            "INR",
            [
                new(SellerA, Diya, "A-1", "Gobar Diya", 1000m, 1, Discount: 100m),
                new(SellerB, Cake, "B-1", "Gobar Cake", 50m, 1, Discount: 5m),
            ],
            deliveryFee: 0m,
            ("WELCOME", "Seller"),
            Guid.NewGuid(),
            Now,
            couponMinOrder: 1040m);
        order.ConfirmPayment(945m, "pay_1", Now);

        foreach (var part in order.Parts)
        {
            order.AdvancePart(part.PublicId, OrderPartStatus.Delivered, Now, Window);
        }

        var b = PartOf(order, SellerB);
        order.RequestReturn(b.PublicId, ReturnReason.NoLongerNeeded, null, null, Now);
        order.ClearDomainEvents();
        order.ApproveReturn(b.PublicId, null, "seller", Now);

        // The cake refunds 45; 45 of the diya's 100 off is taken back, not all of it.
        b.ReturnRequest!.RefundDue.ShouldBe(0m);
        PartOf(order, SellerA).KeptDiscount.ShouldBe(55m);
        order.DomainEvents.OfType<OrderPartDiscountRevokedDomainEvent>().Single().Amount.ShouldBe(45m);
    }

    private static OrderPart PartOf(Order order, Guid seller) => order.Parts.Single(p => p.SellerId == seller);

    /// <summary>
    /// Paid online and delivered. Seller A: three diya packs at 100 (30 off) and a dhoop at 50 (5
    /// off). Seller B: a 100 cake (10 off). A 10% coupon, by <paramref name="fundedBy"/>.
    /// </summary>
    private static Order Delivered(string fundedBy, decimal? minimum, bool deliverB = true)
    {
        var order = Order.Place(
            OrderNumber.New(Now),
            Guid.NewGuid(),
            PaymentMethod.Online,
            DeliveryAddress.Create("Asha Devi", "9876543210", "12 Gaushala Road", null, null, "Lucknow", null, "uttar pradesh", "226001"),
            "INR",
            [
                new(SellerA, Diya, "A-1", "Gobar Diya", 100m, 3, Discount: 30m),
                new(SellerA, Dhoop, "A-2", "Dhoop Batti", 50m, 1, Discount: 5m),
                new(SellerB, Cake, "B-1", "Gobar Cake", 100m, 1, Discount: 10m),
            ],
            deliveryFee: 0m,
            ("WELCOME", fundedBy),
            Guid.NewGuid(),
            Now,
            couponMinOrder: minimum);

        order.ConfirmPayment(405m, "pay_1", Now).IsSuccess.ShouldBeTrue();

        foreach (var part in order.Parts.Where(p => deliverB || p.SellerId != SellerB))
        {
            order.AdvancePart(part.PublicId, OrderPartStatus.Delivered, Now, Window).IsSuccess.ShouldBeTrue();
        }

        return order;
    }
}
