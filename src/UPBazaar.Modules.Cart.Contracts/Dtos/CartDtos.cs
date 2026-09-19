namespace UPBazaar.Modules.Cart.Contracts.Dtos;

/// <summary>
/// A buyer's cart as it stands right now: current prices, current stock, and a flag on every
/// line that cannot be bought as it is.
/// </summary>
/// <param name="Lines">In the order they were added.</param>
/// <param name="Subtotal">Sum of the lines that can be bought. Problem lines are left out, not guessed at.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="ItemCount">Units across all lines, for a header badge.</param>
/// <param name="CanCheckOut">True when there is at least one line and no line has a problem.</param>
public sealed record CartDto(
    IReadOnlyList<CartLineDto> Lines,
    decimal Subtotal,
    string Currency,
    int ItemCount,
    bool CanCheckOut);

/// <summary>One product in the cart.</summary>
/// <param name="ProductId">Catalog public id.</param>
/// <param name="Sku">Stock-keeping unit, or null if the product no longer exists.</param>
/// <param name="Name">Listing title, or null if the product no longer exists.</param>
/// <param name="Quantity">Units wanted.</param>
/// <param name="UnitPrice">Current price.</param>
/// <param name="PriceWhenAdded">What it cost when the buyer last chose it.</param>
/// <param name="LineTotal">Quantity times the current price.</param>
/// <param name="AvailableQuantity">Units in stock right now.</param>
/// <param name="Problem">
/// Null when the line can be bought. Otherwise Unavailable (withdrawn or no longer exists),
/// InsufficientStock (fewer in stock than wanted) or PriceChanged (the price moved since it was
/// added; confirm to clear).
/// </param>
public sealed record CartLineDto(
    Guid ProductId,
    string? Sku,
    string? Name,
    int Quantity,
    decimal UnitPrice,
    decimal PriceWhenAdded,
    decimal LineTotal,
    int AvailableQuantity,
    string? Problem);

/// <summary>What happened to each line of a guest basket merged at sign-in.</summary>
/// <param name="Cart">The cart after the merge.</param>
/// <param name="Skipped">Product ids that were not added because they are not on sale.</param>
public sealed record CartMergeResultDto(CartDto Cart, IReadOnlyList<Guid> Skipped);

/// <summary>A line as checkout takes it: what to buy, at the price the buyer has seen.</summary>
public sealed record CheckoutLineDto(Guid ProductId, int Quantity, decimal UnitPrice, string Currency, Guid SellerId);
