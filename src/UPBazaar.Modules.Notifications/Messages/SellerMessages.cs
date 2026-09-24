using Microsoft.Extensions.Options;
using UPBazaar.Modules.Catalog.Contracts.Events;
using UPBazaar.Modules.Notifications.Delivery;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.Modules.Sellers.Contracts.Events;
using UPBazaar.Modules.Settlements.Contracts.Events;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Notifications.Messages;

/// <summary>
/// Emails to sellers: work to do, and news about their shop and money. Each ends with a link to
/// the page in the seller portal where they act on it.
/// </summary>
internal sealed class SellerEmails(ISellerDirectory sellers, Notifier notifier, IOptions<NotificationsModuleOptions> options)
{
    public string Portal(string path) => $"{options.Value.SellerPortalUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    /// <summary>Emails a seller. A seller that no longer exists is left alone.</summary>
    public async Task SendAsync(
        Guid sellerId,
        string template,
        string key,
        string subject,
        Func<string, string> htmlBody,
        CancellationToken cancellationToken)
    {
        var contact = await sellers.GetContactAsync(sellerId, cancellationToken);

        if (contact is null)
        {
            return;
        }

        await notifier.EmailAsync(key, template, contact.Email, subject, htmlBody(contact.ShopName), cancellationToken);
    }
}

/// <summary>An order is confirmed: every seller with a parcel in it has packing to do.</summary>
internal sealed class OrderConfirmedSellerEmail(IOrderDirectory orders, SellerEmails emails)
    : IDomainEventHandler<OrderConfirmedDomainEvent>
{
    public async Task HandleAsync(OrderConfirmedDomainEvent e, CancellationToken cancellationToken)
    {
        var order = await orders.GetNoticeAsync(e.OrderId, cancellationToken);

        if (order.IsFailure)
        {
            return;
        }

        foreach (var part in order.Value.Parts)
        {
            await emails.SendAsync(
                part.SellerId,
                "order-to-pack",
                $"order-to-pack:{e.EventId}:{part.PartId}",
                $"New order {order.Value.Number} to pack",
                shop => Wording.Email(
                    $"Hello {shop},",
                    [
                        $"You have a new order to pack: {part.ItemCount} item(s) worth {Wording.EmailMoney(part.Subtotal)}"
                        + (order.Value.PaymentMethod == "CashOnDelivery" ? ", cash on delivery." : ", paid online."),
                        "Pack it and book the courier from the seller portal.",
                    ],
                    $"Open order {order.Value.Number}",
                    emails.Portal($"orders/{e.OrderId}")),
                cancellationToken);
        }
    }
}

internal sealed class ReturnRequestedSellerEmail(IOrderDirectory orders, SellerEmails emails)
    : IDomainEventHandler<OrderPartReturnRequestedDomainEvent>
{
    public async Task HandleAsync(OrderPartReturnRequestedDomainEvent e, CancellationToken cancellationToken)
    {
        var order = await orders.GetNoticeAsync(e.OrderId, cancellationToken);
        var part = order.IsSuccess ? order.Value.Parts.FirstOrDefault(p => p.PartId == e.PartId) : null;

        await emails.SendAsync(
            e.SellerId,
            "return-requested",
            $"return-requested:{e.EventId}",
            $"A buyer wants to return a parcel from order {e.Number}",
            shop => Wording.Email(
                $"Hello {shop},",
                [
                    $"The buyer of order {Wording.Html(e.Number)} has asked to return their parcel"
                    + (part is null ? "." : $", worth {Wording.EmailMoney(part.Subtotal)}.")
                    + $" Their reason: {Wording.Html(ReasonText(e.Reason))}.",
                    "Please accept or refuse it soon. If you refuse, say why; the buyer sees your note.",
                ],
                "Decide the return",
                emails.Portal($"orders/{e.OrderId}")),
            cancellationToken);
    }

    private static string ReasonText(string reason) => reason switch
    {
        "Damaged" => "it arrived damaged",
        "WrongItem" => "they received the wrong item",
        "NotAsDescribed" => "it is not as described",
        "QualityIssue" => "poor quality or spoilt",
        "NoLongerNeeded" => "they no longer need it",
        _ => "something else - see their comment in the portal",
    };
}

internal sealed class SellerApprovedEmail(SellerEmails emails) : IDomainEventHandler<SellerApprovedDomainEvent>
{
    public Task HandleAsync(SellerApprovedDomainEvent e, CancellationToken cancellationToken) =>
        emails.SendAsync(
            e.SellerId,
            "seller-approved",
            $"seller-approved:{e.EventId}",
            "Your shop is approved on UP Bazaar",
            shop => Wording.Email(
                $"Hello {shop},",
                ["Your seller application is approved. You can now add products; a moderator checks each listing before it goes live."],
                "Open the seller portal",
                emails.Portal("products")),
            cancellationToken);
}

internal sealed class SellerRejectedEmail(SellerEmails emails) : IDomainEventHandler<SellerRejectedDomainEvent>
{
    public Task HandleAsync(SellerRejectedDomainEvent e, CancellationToken cancellationToken) =>
        emails.SendAsync(
            e.SellerId,
            "seller-rejected",
            $"seller-rejected:{e.EventId}",
            "Your UP Bazaar seller application needs changes",
            shop => Wording.Email(
                $"Hello {shop},",
                [
                    $"Your seller application needs changes before we can approve it: {Wording.Html(e.Note)}",
                    "Correct it and submit it again.",
                ],
                "Update your application",
                emails.Portal("apply")),
            cancellationToken);
}

internal sealed class ProductSentBackEmail(SellerEmails emails) : IDomainEventHandler<ProductSentBackDomainEvent>
{
    public Task HandleAsync(ProductSentBackDomainEvent e, CancellationToken cancellationToken) =>
        emails.SendAsync(
            e.SellerId,
            "listing-sent-back",
            $"listing-sent-back:{e.EventId}",
            $"Your listing \"{e.Name}\" needs changes",
            shop => Wording.Email(
                $"Hello {shop},",
                [
                    $"A moderator sent your listing \"{Wording.Html(e.Name)}\" back: {Wording.Html(e.Note)}",
                    "Edit it and submit it for review again.",
                ],
                "Edit the listing",
                emails.Portal($"products/{e.ProductId}")),
            cancellationToken);
}

internal sealed class PayoutPaidEmail(SellerEmails emails) : IDomainEventHandler<PayoutPaidDomainEvent>
{
    public Task HandleAsync(PayoutPaidDomainEvent e, CancellationToken cancellationToken) =>
        emails.SendAsync(
            e.SellerId,
            "payout-paid",
            $"payout-paid:{e.EventId}",
            $"{Wording.EmailMoney(e.NetAmount)} sent to your bank account",
            shop => Wording.Email(
                $"Hello {shop},",
                [
                    $"We have sent {Wording.EmailMoney(e.NetAmount)} to your bank account for {e.EarningCount} parcel(s).",
                    $"Bank transaction reference (UTR): {Wording.Html(e.Utr)}. It usually arrives within a working day.",
                ],
                "See your earnings",
                emails.Portal("earnings")),
            cancellationToken);
}
