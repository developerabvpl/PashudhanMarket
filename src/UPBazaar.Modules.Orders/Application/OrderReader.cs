using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>Loads orders whole: parts and lines, which every order operation needs.</summary>
internal sealed class OrderReader(UPBazaarDbContext dbContext)
{
    private IQueryable<Order> Orders =>
        dbContext.Set<Order>().Include(o => o.Parts).ThenInclude(p => p.Lines);

    public Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
        Orders.FirstOrDefaultAsync(o => o.PublicId == orderId, cancellationToken);

    /// <summary>
    /// An order only if it belongs to this buyer. Somebody else's comes back null, exactly like
    /// one that does not exist, so ids cannot be probed.
    /// </summary>
    public Task<Order?> FindForBuyerAsync(Guid orderId, Guid buyerId, CancellationToken cancellationToken) =>
        Orders.FirstOrDefaultAsync(o => o.PublicId == orderId && o.BuyerId == buyerId, cancellationToken);

    public Task<Order?> FindReadOnlyAsync(Guid orderId, Guid? buyerId, CancellationToken cancellationToken) =>
        Orders.AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                o => o.PublicId == orderId && (buyerId == null || o.BuyerId == buyerId),
                cancellationToken);

    /// <summary>
    /// A page of orders, newest first, projected straight to summaries.
    ///
    /// The total is written out once and then reused: the refund is what was paid less that
    /// total, by the same rule as <see cref="Order.RefundTotal"/>, and spelling the total out twice
    /// would let the two drift apart.
    /// </summary>
    public static IQueryable<OrderSummaryDto> Summaries(IQueryable<Order> orders) =>
        orders
            .OrderByDescending(o => o.PlacedAtUtc)
            .ThenByDescending(o => o.Id)
            .Select(o => new
            {
                Order = o,
                // What the buyer keeps: not cancelled, not taken back undelivered, less returned units.
                Total = o.Parts
                    .Where(p => p.Status != OrderPartStatus.Cancelled
                        && ((p.Status != OrderPartStatus.Returning && p.Status != OrderPartStatus.Returned)
                            || (p.ReturnRequest != null && p.ReturnRequest.Status == ReturnRequestStatus.Approved)))
                    .SelectMany(p => p.Lines)
                    .Sum(l => (l.UnitPrice * (l.Quantity - l.ReturnedQuantity)) - (l.Discount - l.ReturnedDiscount - l.RevokedDiscount))
                    + o.ShippingFee
                    - o.Parts.Where(p => p.FreeDelivery).Sum(p => p.DeliveryFee),
            })
            .Select(x => new OrderSummaryDto(
                x.Order.PublicId,
                x.Order.Number,
                x.Order.Status.ToString(),
                x.Order.PaymentMethod.ToString(),
                x.Order.PaymentStatus.ToString(),
                x.Total,
                x.Order.Currency,
                x.Order.Parts.SelectMany(p => p.Lines).Sum(l => l.Quantity),
                x.Order.PlacedAtUtc,
                x.Order.Parts.OrderBy(p => p.Id).Select(p => p.Status.ToString()).ToList(),
                x.Order.AmountPaid,
                x.Order.AmountPaid != null && x.Order.AmountPaid > x.Total ? x.Order.AmountPaid.Value - x.Total : 0m,
                // The web's order-status rule reads these beside the statuses; it works the same
                // answer out of an order's parts on the order's own page (isPartialReturn there).
                x.Order.Parts.OrderBy(p => p.Id)
                    .Select(p =>
                        p.ReturnRequest == null
                        || p.ReturnRequest.Status != ReturnRequestStatus.Approved
                        || (p.Status != OrderPartStatus.Returning && p.Status != OrderPartStatus.Returned)
                            ? "None"
                            : p.Lines.Any(l => l.ReturnRequestedQuantity > 0) && p.Lines.Any(l => l.ReturnRequestedQuantity < l.Quantity)
                                ? "Partial"
                                : "Full")
                    .ToList()));
}

/// <summary>Maps orders to the DTOs the API and other modules see.</summary>
internal static class OrderMappings
{
    public static OrderDto ToDto(this Order order) => new(
        order.PublicId,
        order.Number,
        order.BuyerId,
        order.Status.ToString(),
        order.PaymentMethod.ToString(),
        order.PaymentStatus.ToString(),
        order.Subtotal,
        order.Discount,
        order.ShippingFee,
        order.DeliveryDiscount,
        order.Total,
        order.Currency,
        order.DeliveryAddress.ToDto(),
        [.. order.Parts.OrderBy(p => p.Id).Select(ToDto)],
        order.PlacedAtUtc,
        order.PaymentDueAtUtc,
        order.PaymentReference,
        order.CancelledAtUtc,
        order.CancellationReason,
        order.CanCancel,
        order.CouponCode,
        order.AmountPaid,
        order.RefundTotal);

    private static OrderPartDto ToDto(OrderPart part) => new(
        part.PublicId,
        part.SellerId,
        part.Status.ToString(),
        part.Subtotal,
        part.Discount,
        [.. part.Lines.OrderBy(l => l.Id).Select(l =>
            new OrderLineDto(l.ProductId, l.Sku, l.Name, l.UnitPrice, l.Quantity, l.LineTotal, l.Discount, l.ReturnRequestedQuantity, l.ReturnCondition?.ToString()))],
        part.CancellationReason,
        part.ReturnCondition?.ToString(),
        part.DeliveredAtUtc,
        part.ReturnWindowClosesAtUtc,
        part.ReturnRequest?.ToDto(includeUpiId: true));

    /// <param name="request">The request.</param>
    /// <param name="includeUpiId">False for sellers: the buyer's UPI id is for the platform's refund, not them.</param>
    public static ReturnRequestDto ToDto(this ReturnRequest request, bool includeUpiId) => new(
        request.Status.ToString(),
        request.Reason.ToString(),
        request.Comment,
        includeUpiId ? request.RefundUpiId : null,
        request.RequestedAtUtc,
        request.DecisionNote,
        request.DecidedAtUtc,
        request.RefundDue);

    private static DeliveryAddressDto ToDto(this DeliveryAddress address) => new(
        address.FullName,
        address.Mobile,
        address.Line1,
        address.Line2,
        address.Landmark,
        address.City,
        address.District,
        address.State,
        address.Pincode);
}
