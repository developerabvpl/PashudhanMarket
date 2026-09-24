using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Contracts;

/// <summary>
/// What other modules may read about an order to tell people about it: who bought it, how to
/// reach them, and whose parcels are in it. Notifications uses it; it never sees addresses or lines.
/// </summary>
public interface IOrderDirectory
{
    /// <summary>The order's outline. Not found for an id that matches no order.</summary>
    Task<Result<OrderNoticeDto>> GetNoticeAsync(Guid orderId, CancellationToken cancellationToken);
}

/// <summary>An order, as much as a message about it needs.</summary>
/// <param name="OrderId">Public id.</param>
/// <param name="Number">The order number buyers and sellers quote.</param>
/// <param name="BuyerId">Identity's id for the buyer.</param>
/// <param name="DeliveryMobile">The mobile given at checkout, for when the buyer's account has none.</param>
/// <param name="Total">What the buyer pays for what is still coming.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="PaymentMethod">CashOnDelivery or Online.</param>
/// <param name="Parts">One per seller.</param>
public sealed record OrderNoticeDto(
    Guid OrderId,
    string Number,
    Guid BuyerId,
    string DeliveryMobile,
    decimal Total,
    string Currency,
    string PaymentMethod,
    IReadOnlyList<OrderNoticePartDto> Parts);

/// <summary>One seller's parcel in an order.</summary>
/// <param name="PartId">Public id.</param>
/// <param name="SellerId">Whose parcel.</param>
/// <param name="Subtotal">Its value.</param>
/// <param name="ItemCount">Units in it.</param>
public sealed record OrderNoticePartDto(Guid PartId, Guid SellerId, decimal Subtotal, int ItemCount);
