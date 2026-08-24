using UPBazaar.Modules.Ordering.Contracts.Events;
using UPBazaar.SharedKernel.Results;
using UPBazaar.Modules.Ordering.Domain;

namespace UPBazaar.UnitTests.Ordering;

public sealed class OrderTests
{
    private static readonly DateTime PlacedAt = new(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc);

    private static Result<Order> PlaceOrder(params (Guid, string, string, decimal, int)[] lines) =>
        Order.Place(
            Guid.NewGuid(),
            "UPB-20260314-ABCD1234",
            "221001",
            "INR",
            shippingFee: 49m,
            lines,
            PlacedAt);

    [Fact]
    public void Place_totals_lines_and_adds_the_shipping_fee()
    {
        var order = PlaceOrder(
            (Guid.NewGuid(), "SKU-1", "Saree", 1200.50m, 2),
            (Guid.NewGuid(), "SKU-2", "Dupatta", 300.00m, 1)).Value;

        order.Subtotal.ShouldBe(2701.00m);
        order.ShippingFee.ShouldBe(49m);
        order.Total.ShouldBe(2750.00m);
        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
    }

    [Fact]
    public void Place_rejects_an_empty_cart()
    {
        var result = PlaceOrder();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(OrderErrors.EmptyCart);
    }

    [Fact]
    public void Place_raises_the_event_shipping_listens_for()
    {
        var productId = Guid.NewGuid();

        var order = PlaceOrder((productId, "SKU-1", "Saree", 100m, 3)).Value;

        var placed = order.DomainEvents.OfType<OrderPlacedDomainEvent>().ShouldHaveSingleItem();
        placed.OrderId.ShouldBe(order.PublicId);
        placed.DeliveryPostcode.ShouldBe("221001");
        placed.Lines.ShouldHaveSingleItem().ProductId.ShouldBe(productId);
    }

    [Fact]
    public void MarkPaid_is_idempotent_so_a_redelivered_capture_is_harmless()
    {
        var order = PlaceOrder((Guid.NewGuid(), "SKU-1", "Saree", 100m, 1)).Value;
        var paymentId = Guid.NewGuid();

        order.MarkPaid(paymentId).IsSuccess.ShouldBeTrue();
        order.ClearDomainEvents();

        order.MarkPaid(paymentId).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Paid);
        order.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Cancel_is_refused_once_the_order_is_paid()
    {
        var order = PlaceOrder((Guid.NewGuid(), "SKU-1", "Saree", 100m, 1)).Value;
        order.MarkPaid(Guid.NewGuid());

        order.Cancel("Changed mind").Error.ShouldBe(OrderErrors.NotCancellable);
    }

    [Fact]
    public void Cancel_reports_the_lines_so_stock_can_be_released()
    {
        var productId = Guid.NewGuid();
        var order = PlaceOrder((productId, "SKU-1", "Saree", 100m, 4)).Value;
        order.ClearDomainEvents();

        order.Cancel("Out of delivery area").IsSuccess.ShouldBeTrue();

        var cancelled = order.DomainEvents.OfType<OrderCancelledDomainEvent>().ShouldHaveSingleItem();
        cancelled.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(4);
    }

    [Fact]
    public void Order_numbers_are_dated_and_unique()
    {
        var first = Order.NextOrderNumber(PlacedAt);
        var second = Order.NextOrderNumber(PlacedAt);

        first.ShouldStartWith("UPB-20260314-");
        first.ShouldNotBe(second);
    }
}
