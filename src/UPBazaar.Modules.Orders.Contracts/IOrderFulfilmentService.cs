using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Contracts;

/// <summary>
/// What Shipping needs from Orders to send one seller's part of an order: what is in it and where
/// it goes, and a way to move it along as the courier reports progress.
/// </summary>
public interface IOrderFulfilmentService
{
    /// <summary>One seller's part with everything a courier booking needs. Not found if either id is wrong.</summary>
    Task<Result<ShippablePartDto>> GetPartAsync(Guid orderId, Guid partId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a part to Packed, Shipped or Delivered, or - when the courier could not deliver - to
    /// Returning and then Returned; or, when a buyer's return reaches the seller, to Returned. A
    /// part already there or further along is
    /// left as it is and reported as success, because courier updates arrive late, twice, and out
    /// of order; only a move the part can never make - out of Cancelled, say - is refused.
    /// </summary>
    Task<Result> AdvancePartAsync(Guid orderId, Guid partId, string status, CancellationToken cancellationToken);
}

/// <summary>One seller's part of an order, as a shipment sees it.</summary>
/// <param name="OrderId">Public id of the order.</param>
/// <param name="OrderNumber">The order number, used as the courier booking's reference.</param>
/// <param name="BuyerId">Who ordered it.</param>
/// <param name="PartId">Public id of the part.</param>
/// <param name="SellerId">Whose goods these are; decides the pickup location.</param>
/// <param name="Status">The part's status: AwaitingPayment, Confirmed, Packed, Shipped, Delivered or Cancelled.</param>
/// <param name="PaymentMethod">CashOnDelivery or Online.</param>
/// <param name="CodAmount">What the courier collects at the door: the part's goods and its share of delivery for cash on delivery, else zero.</param>
/// <param name="Subtotal">Value of the goods in the part, for the courier's declared value.</param>
/// <param name="DeliveryFee">The part's share of the delivery charge the buyer pays.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="PlacedAtUtc">When the order was placed.</param>
/// <param name="DeliveryAddress">Where it goes.</param>
/// <param name="Lines">What is in it.</param>
/// <param name="BuyerReturnApproved">
/// True when the part is going back because the buyer's return was approved: a courier must
/// collect it from the delivery address. False for an RTO, which the courier brings back itself.
/// </param>
public sealed record ShippablePartDto(
    Guid OrderId,
    string OrderNumber,
    Guid BuyerId,
    Guid PartId,
    Guid SellerId,
    string Status,
    string PaymentMethod,
    decimal CodAmount,
    decimal Subtotal,
    decimal DeliveryFee,
    string Currency,
    DateTime PlacedAtUtc,
    DeliveryAddressDto DeliveryAddress,
    IReadOnlyList<OrderLineDto> Lines,
    bool BuyerReturnApproved);
