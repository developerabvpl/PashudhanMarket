using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Ordering.Domain;

/// <summary>
/// A priced snapshot taken at checkout. Prices are copied, never looked up later, so a
/// catalog price change cannot alter an order that is already placed.
/// </summary>
public sealed class OrderLine : Entity
{
    private OrderLine()
    {
    }

    public long OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    internal static OrderLine Create(Guid productId, string sku, string name, decimal unitPrice, int quantity) =>
        new()
        {
            ProductId = productId,
            Sku = sku,
            Name = name,
            UnitPrice = unitPrice,
            Quantity = quantity,
        };
}
