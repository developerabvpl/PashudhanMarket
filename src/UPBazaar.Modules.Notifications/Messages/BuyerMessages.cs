using Microsoft.Extensions.Options;
using UPBazaar.Modules.Identity.Contracts;
using UPBazaar.Modules.Notifications.Delivery;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Payments.Contracts.Events;
using UPBazaar.Modules.Reviews.Contracts.Events;
using UPBazaar.Modules.Shipping.Contracts.Events;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Notifications.Messages;

/// <summary>
/// Texts to buyers as their order moves. Each is short enough for one SMS and ends with a link to
/// the order, where the detail is. English for now; the platform does not yet record which
/// language a buyer reads.
/// </summary>
internal sealed class BuyerTexts(
    IOrderDirectory orders,
    IUserDirectory users,
    Notifier notifier,
    IOptions<NotificationsModuleOptions> options)
{
    /// <summary>Texts the buyer of an order. An order that no longer exists is left alone.</summary>
    public async Task SendAsync(
        Guid orderId,
        string template,
        string key,
        Func<OrderNoticeDto, string, string> body,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetNoticeAsync(orderId, cancellationToken);

        if (order.IsFailure)
        {
            return;
        }

        var link = $"{options.Value.StorefrontUrl.TrimEnd('/')}/orders/{order.Value.OrderId}";

        await notifier.SmsAsync(key, template, await MobileAsync(order.Value, cancellationToken), body(order.Value, link), cancellationToken);
    }

    /// <summary>
    /// The mobile on the buyer's account - the number they sign in with, and so the one they
    /// watch - or the one given at checkout if the account has none.
    /// </summary>
    private async Task<string> MobileAsync(OrderNoticeDto order, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(order.BuyerId, cancellationToken);

        return user.IsSuccess && !string.IsNullOrWhiteSpace(user.Value.Mobile) ? user.Value.Mobile : order.DeliveryMobile;
    }
}

internal sealed class OrderConfirmedBuyerText(BuyerTexts texts) : IDomainEventHandler<OrderConfirmedDomainEvent>
{
    public Task HandleAsync(OrderConfirmedDomainEvent e, CancellationToken cancellationToken) =>
        texts.SendAsync(e.OrderId, "order-confirmed", $"order-confirmed:{e.EventId}", (order, link) =>
            $"UP Bazaar: order {order.Number} is confirmed, {Wording.SmsMoney(order.Total)}. "
            + $"We will text you when it ships. {link}", cancellationToken);
}

internal sealed class ShipmentDispatchedBuyerText(BuyerTexts texts) : IDomainEventHandler<ShipmentDispatchedDomainEvent>
{
    public Task HandleAsync(ShipmentDispatchedDomainEvent e, CancellationToken cancellationToken) =>
        texts.SendAsync(e.OrderId, "parcel-dispatched", $"parcel-dispatched:{e.EventId}", (order, link) =>
            $"UP Bazaar: a parcel from order {order.Number} is on its way"
            + (e.CourierName is null ? string.Empty : $" with {e.CourierName}")
            + (e.Awb is null ? string.Empty : $", AWB {e.Awb}")
            + $". Track it: {link}", cancellationToken);
}

internal sealed class OrderPartDeliveredBuyerText(BuyerTexts texts) : IDomainEventHandler<OrderPartDeliveredDomainEvent>
{
    public Task HandleAsync(OrderPartDeliveredDomainEvent e, CancellationToken cancellationToken) =>
        texts.SendAsync(e.OrderId, "parcel-delivered", $"parcel-delivered:{e.EventId}", (order, link) =>
            $"UP Bazaar: a parcel from order {order.Number} was delivered. Something wrong? "
            + $"You can ask to return it until {Wording.Date(e.ReturnWindowClosesAtUtc)}: {link}", cancellationToken);
}

internal sealed class OrderCancelledBuyerText(BuyerTexts texts) : IDomainEventHandler<OrderCancelledDomainEvent>
{
    public Task HandleAsync(OrderCancelledDomainEvent e, CancellationToken cancellationToken) =>
        texts.SendAsync(e.OrderId, "order-cancelled", $"order-cancelled:{e.EventId}", (order, link) =>
            $"UP Bazaar: order {order.Number} is cancelled. "
            + (order.PaymentMethod == "Online" ? "Anything you paid online will be refunded. " : string.Empty)
            + link, cancellationToken);
}

internal sealed class ReturnApprovedBuyerText(BuyerTexts texts) : IDomainEventHandler<OrderPartReturnApprovedDomainEvent>
{
    public Task HandleAsync(OrderPartReturnApprovedDomainEvent e, CancellationToken cancellationToken) =>
        texts.SendAsync(e.OrderId, "return-approved", $"return-approved:{e.EventId}", (order, link) =>
            $"UP Bazaar: your return from order {order.Number} is accepted. A courier will collect the parcel "
            + $"from your delivery address. {link}", cancellationToken);
}

internal sealed class ReturnRejectedBuyerText(BuyerTexts texts) : IDomainEventHandler<OrderPartReturnRejectedDomainEvent>
{
    public Task HandleAsync(OrderPartReturnRejectedDomainEvent e, CancellationToken cancellationToken) =>
        texts.SendAsync(e.OrderId, "return-rejected", $"return-rejected:{e.EventId}", (order, link) =>
            $"UP Bazaar: the seller did not accept your return from order {order.Number}. See why: {link}", cancellationToken);
}

internal sealed class RefundMadeBuyerText(BuyerTexts texts) : IDomainEventHandler<RefundMadeDomainEvent>
{
    public Task HandleAsync(RefundMadeDomainEvent e, CancellationToken cancellationToken) =>
        texts.SendAsync(e.OrderId, "refund-made", $"refund-made:{e.EventId}", (order, link) =>
            $"UP Bazaar: we have refunded {Wording.SmsMoney(e.Amount)} for order {order.Number}"
            + (e.UpiId is null
                ? " to your original payment. It can take a few working days to reach you. "
                : $" to {e.UpiId}. ")
            + link, cancellationToken);
}

internal sealed class ReviewRejectedBuyerText(BuyerTexts texts) : IDomainEventHandler<ReviewRejectedDomainEvent>
{
    // The staff note stays on the order page: at up to 500 characters it would not fit one SMS.
    public Task HandleAsync(ReviewRejectedDomainEvent e, CancellationToken cancellationToken) =>
        texts.SendAsync(e.OrderId, "review-rejected", $"review-rejected:{e.EventId}", (_, link) =>
            $"UP Bazaar: we could not publish the words of your review of {Wording.Short(e.ProductName, 40)}. "
            + $"Your stars still count. See why and change it: {link}", cancellationToken);
}
