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

    /// <summary>A page of orders, newest first, projected straight to summaries.</summary>
    public static IQueryable<OrderSummaryDto> Summaries(IQueryable<Order> orders) =>
        orders
            .OrderByDescending(o => o.PlacedAtUtc)
            .ThenByDescending(o => o.Id)
            .Select(o => new OrderSummaryDto(
                o.PublicId,
                o.Number,
                o.Status.ToString(),
                o.PaymentMethod.ToString(),
                o.PaymentStatus.ToString(),
                o.Parts
                    .Where(p => p.Status != OrderPartStatus.Cancelled && p.Status != OrderPartStatus.Returning && p.Status != OrderPartStatus.Returned)
                    .SelectMany(p => p.Lines)
                    .Sum(l => (l.UnitPrice * l.Quantity) - l.Discount)
                    + o.ShippingFee
                    - o.Parts.Where(p => p.FreeDelivery).Sum(p => p.DeliveryFee),
                o.Currency,
                o.Parts.SelectMany(p => p.Lines).Sum(l => l.Quantity),
                o.PlacedAtUtc));
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
        order.CouponCode);

    private static OrderPartDto ToDto(OrderPart part) => new(
        part.PublicId,
        part.SellerId,
        part.Status.ToString(),
        part.Subtotal,
        part.Discount,
        [.. part.Lines.OrderBy(l => l.Id).Select(l =>
            new OrderLineDto(l.ProductId, l.Sku, l.Name, l.UnitPrice, l.Quantity, l.LineTotal, l.Discount))],
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
        request.DecidedAtUtc);

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
