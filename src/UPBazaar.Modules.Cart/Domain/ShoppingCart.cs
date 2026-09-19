using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Cart.Domain;

/// <summary>
/// One buyer's cart: which products, how many, and what each cost when chosen.
///
/// It holds no names, no stock and no current prices. Those belong to Catalog and Inventory and
/// are read fresh every time the cart is shown, so a cart can never display a figure that has
/// since changed. The one price it does keep, <see cref="CartLine.PriceWhenAdded"/>, exists only
/// to notice that change and tell the buyer.
///
/// Named ShoppingCart rather than Cart so it does not collide with the module's namespace.
/// </summary>
public sealed class ShoppingCart : Entity, IAuditable
{
    /// <summary>Most units of one product in a cart. Anything larger is a stuck key or a wholesale order.</summary>
    public const int MaxQuantityPerLine = 99;

    /// <summary>Most distinct products in a cart.</summary>
    public const int MaxLines = 50;

    private readonly List<CartLine> _lines = [];

    private ShoppingCart()
    {
    }

    /// <summary>Identity's public id for the buyer. One cart per buyer.</summary>
    public Guid BuyerId { get; private set; }

    public IReadOnlyCollection<CartLine> Lines => _lines.AsReadOnly();

    /// <summary>Optimistic concurrency token: two tabs editing at once must not silently lose a change.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public static ShoppingCart Create(Guid buyerId) => new() { BuyerId = buyerId };

    /// <summary>
    /// Sets how many of a product the buyer wants. Zero removes the line. The price is the one the
    /// buyer is looking at now, which also acknowledges any earlier price change.
    /// </summary>
    public Result SetQuantity(Guid productId, int quantity, decimal currentPrice)
    {
        if (quantity is < 0 or > MaxQuantityPerLine)
        {
            return Result.Failure(CartErrors.QuantityOutOfRange);
        }

        var line = _lines.FirstOrDefault(l => l.ProductId == productId);

        if (quantity == 0)
        {
            if (line is not null)
            {
                _lines.Remove(line);
            }

            return Result.Success();
        }

        if (line is null)
        {
            if (_lines.Count >= MaxLines)
            {
                return Result.Failure(CartErrors.TooManyLines);
            }

            _lines.Add(CartLine.Create(productId, quantity, currentPrice));
        }
        else
        {
            line.Update(quantity, currentPrice);
        }

        return Result.Success();
    }

    /// <summary>
    /// Adds a guest basket line on top of whatever is already here, capped rather than refused:
    /// a buyer who signs in with 60 in the basket and 60 in the account wants 99, not an error.
    /// Returns false when the cart is full and the line could not be added.
    /// </summary>
    public bool Merge(Guid productId, int quantity, decimal currentPrice)
    {
        var line = _lines.FirstOrDefault(l => l.ProductId == productId);
        var total = Math.Min(MaxQuantityPerLine, (line?.Quantity ?? 0) + quantity);

        if (line is null && _lines.Count >= MaxLines)
        {
            return false;
        }

        return SetQuantity(productId, total, currentPrice).IsSuccess;
    }

    /// <summary>Accepts the current prices of every line, clearing all PriceChanged flags at once.</summary>
    public void AcknowledgePrices(IReadOnlyDictionary<Guid, decimal> currentPrices)
    {
        ArgumentNullException.ThrowIfNull(currentPrices);

        foreach (var line in _lines)
        {
            if (currentPrices.TryGetValue(line.ProductId, out var price))
            {
                line.Update(line.Quantity, price);
            }
        }
    }

    public void Clear() => _lines.Clear();
}

/// <summary>One product in a cart.</summary>
public sealed class CartLine : Entity
{
    private CartLine()
    {
    }

    public long CartId { get; private set; }

    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>The price the buyer last saw for this product, so a later change can be pointed out.</summary>
    public decimal PriceWhenAdded { get; private set; }

    internal static CartLine Create(Guid productId, int quantity, decimal price) => new()
    {
        ProductId = productId,
        Quantity = quantity,
        PriceWhenAdded = price,
    };

    internal void Update(int quantity, decimal price)
    {
        Quantity = quantity;
        PriceWhenAdded = price;
    }
}
