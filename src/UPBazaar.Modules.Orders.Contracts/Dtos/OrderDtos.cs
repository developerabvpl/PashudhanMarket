namespace UPBazaar.Modules.Orders.Contracts.Dtos;

/// <summary>
/// An order as its buyer and staff see it.
///
/// One order per checkout, split into a part per seller, because each seller packs and ships on
/// their own: one part can be delivered while another is still being packed or was cancelled.
/// </summary>
/// <param name="Id">Public id.</param>
/// <param name="Number">Human-readable order number, such as UPB-260921-7K3QX9. Unique.</param>
/// <param name="BuyerId">Identity's public id for the buyer.</param>
/// <param name="Status">PendingPayment, Confirmed, Completed or Cancelled.</param>
/// <param name="PaymentMethod">CashOnDelivery or Online.</param>
/// <param name="PaymentStatus">
/// Pending (online, not paid yet), Paid (online), or CashOnDelivery (to be collected at the door).
/// </param>
/// <param name="Subtotal">Sum of the lines in parts that are not cancelled.</param>
/// <param name="ShippingFee">Delivery charge. Zero until Shipping prices deliveries.</param>
/// <param name="Total">Subtotal plus shipping: what the buyer pays for what is still coming.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="DeliveryAddress">Where it goes, as entered at checkout.</param>
/// <param name="Parts">One per seller.</param>
/// <param name="PlacedAtUtc">When the order was placed.</param>
/// <param name="PaymentDueAtUtc">For an unpaid online order, when it is cancelled if no payment has arrived.</param>
/// <param name="PaymentReference">The payment provider's id for the payment, once paid online.</param>
/// <param name="CancelledAtUtc">When the whole order was cancelled, if it was.</param>
/// <param name="CancellationReason">Why, if it was.</param>
/// <param name="CanCancel">True while the buyer may still cancel: nothing has shipped yet.</param>
public sealed record OrderDto(
    Guid Id,
    string Number,
    Guid BuyerId,
    string Status,
    string PaymentMethod,
    string PaymentStatus,
    decimal Subtotal,
    decimal ShippingFee,
    decimal Total,
    string Currency,
    DeliveryAddressDto DeliveryAddress,
    IReadOnlyList<OrderPartDto> Parts,
    DateTime PlacedAtUtc,
    DateTime? PaymentDueAtUtc,
    string? PaymentReference,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    bool CanCancel);

/// <summary>One seller's share of an order.</summary>
/// <param name="Id">Public id.</param>
/// <param name="SellerId">Public id of the selling account.</param>
/// <param name="Status">AwaitingPayment, Confirmed, Packed, Shipped, Delivered or Cancelled.</param>
/// <param name="Subtotal">Sum of this part's lines.</param>
/// <param name="Lines">What this seller is sending.</param>
/// <param name="CancellationReason">Why it was cancelled, if it was.</param>
public sealed record OrderPartDto(
    Guid Id,
    Guid SellerId,
    string Status,
    decimal Subtotal,
    IReadOnlyList<OrderLineDto> Lines,
    string? CancellationReason);

/// <summary>
/// One product in an order, frozen as it was bought. Name, SKU and price are copied in so the
/// order still reads correctly after the listing is edited or withdrawn.
/// </summary>
public sealed record OrderLineDto(
    Guid ProductId,
    string Sku,
    string Name,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

/// <summary>A delivery address as entered at checkout.</summary>
/// <param name="FullName">Who receives it.</param>
/// <param name="Mobile">Ten-digit Indian mobile number, for the courier.</param>
/// <param name="Line1">House, building, street.</param>
/// <param name="Line2">Area or locality, if any.</param>
/// <param name="Landmark">Something the courier can find it by, if any.</param>
/// <param name="City">City, town or village.</param>
/// <param name="District">District, if any.</param>
/// <param name="State">State or union territory.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
public sealed record DeliveryAddressDto(
    string FullName,
    string Mobile,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    string? District,
    string State,
    string Pincode);

/// <summary>A row in an order list: enough to recognise an order without loading it.</summary>
public sealed record OrderSummaryDto(
    Guid Id,
    string Number,
    string Status,
    string PaymentMethod,
    string PaymentStatus,
    decimal Total,
    string Currency,
    int ItemCount,
    DateTime PlacedAtUtc);

/// <summary>What Payments needs to take money for an order.</summary>
/// <param name="OrderId">Public id.</param>
/// <param name="Number">Order number, for the payment provider's receipt field.</param>
/// <param name="BuyerId">Who must be paying.</param>
/// <param name="Amount">Exact amount due.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="PaymentDueAtUtc">After this the order is cancelled, and a late payment must be refunded.</param>
public sealed record PayableOrderDto(
    Guid OrderId,
    string Number,
    Guid BuyerId,
    decimal Amount,
    string Currency,
    DateTime PaymentDueAtUtc);
