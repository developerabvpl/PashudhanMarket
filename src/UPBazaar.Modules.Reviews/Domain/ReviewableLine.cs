using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Reviews.Domain;

/// <summary>
/// A product that was delivered to a buyer, and so may be reviewed by them.
///
/// Recorded when a parcel is delivered, one row per line, because Orders has no question to ask
/// later of the form "did this buyer receive this product". Nothing ever removes a row: a buyer
/// who returns the parcel may still review what was in it, since a bad product is so often why
/// it went back.
/// </summary>
public sealed class ReviewableLine : Entity
{
    private ReviewableLine()
    {
    }

    public Guid BuyerId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid SellerId { get; private set; }

    /// <summary>The product's name as delivered, so a review reads right even if it is renamed.</summary>
    public string ProductName { get; private set; } = string.Empty;

    public Guid OrderId { get; private set; }

    public Guid OrderPartId { get; private set; }

    public DateTime DeliveredAtUtc { get; private set; }

    public static ReviewableLine Record(
        Guid buyerId,
        Guid productId,
        Guid sellerId,
        string productName,
        Guid orderId,
        Guid orderPartId,
        DateTime deliveredAtUtc) =>
        new()
        {
            BuyerId = buyerId,
            ProductId = productId,
            SellerId = sellerId,
            ProductName = productName,
            OrderId = orderId,
            OrderPartId = orderPartId,
            DeliveredAtUtc = deliveredAtUtc,
        };
}
