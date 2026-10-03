namespace UPBazaar.Modules.Orders.Contracts.Dtos;

/// <summary>
/// A seller's share of one order, in a list: the seller sees their own part and nothing of any
/// other seller's, not even that there is one.
/// </summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="PartId">The seller's part.</param>
/// <param name="Status">The part's status.</param>
/// <param name="PaymentMethod">CashOnDelivery or Online.</param>
/// <param name="CodAmount">What the courier collects at the door for this part; zero when prepaid.</param>
/// <param name="Subtotal">Value of the part.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="ItemCount">Units in the part.</param>
/// <param name="City">Where it goes, for a glance at the list.</param>
/// <param name="PlacedAtUtc">When the order was placed.</param>
/// <param name="ReturnRequestStatus">Requested, Approved or Rejected if the buyer asked to return it; else null.</param>
public sealed record SellerOrderSummaryDto(
    Guid OrderId,
    string OrderNumber,
    Guid PartId,
    string Status,
    string PaymentMethod,
    decimal CodAmount,
    decimal Subtotal,
    string Currency,
    int ItemCount,
    string City,
    DateTime PlacedAtUtc,
    string? ReturnRequestStatus);

/// <summary>
/// A seller's part of one order in full: what to pack and where it goes. ReturnCondition is null
/// until a part that came back has been inspected. ReturnRequest is the buyer's request to send it
/// back, if any, without the buyer's UPI id: refunds are the platform's to pay, not the seller's.
///
/// Subtotal is the part at full price and Discount the coupon's share of it, so the seller sees
/// what the buyer actually pays for the goods. DiscountFundedBy says who bears that discount -
/// Seller (their earnings are worked out on the discounted price) or Platform (they are paid on
/// the full price) - and is null when no coupon touched the part.
/// </summary>
public sealed record SellerOrderDto(
    Guid OrderId,
    string OrderNumber,
    Guid PartId,
    string Status,
    string PaymentMethod,
    decimal CodAmount,
    decimal Subtotal,
    string Currency,
    DateTime PlacedAtUtc,
    DeliveryAddressDto DeliveryAddress,
    IReadOnlyList<OrderLineDto> Lines,
    string? CancellationReason,
    string? ReturnCondition,
    ReturnRequestDto? ReturnRequest,
    decimal Discount = 0m,
    string? DiscountFundedBy = null);
