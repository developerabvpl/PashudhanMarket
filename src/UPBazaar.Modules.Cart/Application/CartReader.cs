using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Cart.Contracts.Dtos;
using UPBazaar.Modules.Cart.Domain;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.Modules.Inventory.Contracts.Dtos;

namespace UPBazaar.Modules.Cart.Application;

/// <summary>Why a line cannot be bought as it stands.</summary>
internal static class LineProblem
{
    public const string Unavailable = nameof(Unavailable);
    public const string InsufficientStock = nameof(InsufficientStock);
    public const string PriceChanged = nameof(PriceChanged);
}

/// <summary>
/// Turns a stored cart into what the buyer sees, by asking Catalog for today's prices and
/// Inventory for today's stock. Every read is fresh; nothing here is cached.
/// </summary>
internal sealed class CartReader(UPBazaarDbContext dbContext, IProductCatalog catalog, IInventoryService inventory)
{
    public const string DefaultCurrency = "INR";

    public Task<ShoppingCart?> FindAsync(Guid buyerId, CancellationToken cancellationToken) =>
        dbContext.Set<ShoppingCart>()
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId, cancellationToken);

    /// <summary>The buyer's cart, creating an empty one on first use.</summary>
    public async Task<ShoppingCart> FindOrCreateAsync(Guid buyerId, CancellationToken cancellationToken)
    {
        var cart = await FindAsync(buyerId, cancellationToken);

        if (cart is null)
        {
            cart = ShoppingCart.Create(buyerId);
            dbContext.Set<ShoppingCart>().Add(cart);
        }

        return cart;
    }

    public async Task<(IReadOnlyDictionary<Guid, CatalogProductDto> Products, IReadOnlyDictionary<Guid, StockLevelDto> Stock)>
        LookUpAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return (new Dictionary<Guid, CatalogProductDto>(), new Dictionary<Guid, StockLevelDto>());
        }

        var products = await catalog.GetProductsAsync(productIds, cancellationToken);
        var stock = await inventory.GetStockLevelsAsync(productIds, cancellationToken);

        return (products, stock);
    }

    /// <summary>The cart as the API shows it. A buyer with no cart yet gets an empty one.</summary>
    public async Task<CartDto> ToDtoAsync(ShoppingCart? cart, CancellationToken cancellationToken)
    {
        var lines = cart?.Lines.OrderBy(l => l.Id).ToList() ?? [];
        var (products, stock) = await LookUpAsync([.. lines.Select(l => l.ProductId)], cancellationToken);

        var dtos = lines.Select(line =>
        {
            products.TryGetValue(line.ProductId, out var product);
            var available = stock.TryGetValue(line.ProductId, out var level) ? Math.Max(0, level.AvailableQuantity) : 0;
            var price = product?.Price ?? line.PriceWhenAdded;

            var problem =
                product is null || !product.IsOnSale ? LineProblem.Unavailable
                : available < line.Quantity ? LineProblem.InsufficientStock
                : price != line.PriceWhenAdded ? LineProblem.PriceChanged
                : null;

            return new CartLineDto(
                line.ProductId,
                product?.Sku,
                product?.Name,
                line.Quantity,
                price,
                line.PriceWhenAdded,
                price * line.Quantity,
                available,
                problem);
        }).ToList();

        return new CartDto(
            dtos,
            dtos.Where(l => l.Problem is null).Sum(l => l.LineTotal),
            products.Values.FirstOrDefault()?.Currency ?? DefaultCurrency,
            dtos.Sum(l => l.Quantity),
            CanCheckOut: dtos.Count > 0 && dtos.All(l => l.Problem is null));
    }
}
