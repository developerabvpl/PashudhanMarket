using UPBazaar.Modules.Cart.Application;
using UPBazaar.Modules.Cart.Contracts;
using UPBazaar.Modules.Cart.Contracts.Dtos;
using UPBazaar.Modules.Cart.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Cart.Services;

/// <summary>Implements <see cref="ICartService"/> for checkout.</summary>
internal sealed class CartService(CartReader reader) : ICartService
{
    public async Task<Result<IReadOnlyList<CheckoutLineDto>>> GetCheckoutLinesAsync(
        Guid buyerId,
        CancellationToken cancellationToken)
    {
        var cart = await reader.FindAsync(buyerId, cancellationToken);

        if (cart is null || cart.Lines.Count == 0)
        {
            return Result.Failure<IReadOnlyList<CheckoutLineDto>>(CartErrors.Empty);
        }

        var shown = await reader.ToDtoAsync(cart, cancellationToken);

        if (!shown.CanCheckOut)
        {
            return Result.Failure<IReadOnlyList<CheckoutLineDto>>(CartErrors.NotReadyForCheckout);
        }

        var (products, _) = await reader.LookUpAsync([.. cart.Lines.Select(l => l.ProductId)], cancellationToken);

        return Result.Success<IReadOnlyList<CheckoutLineDto>>(
        [
            .. shown.Lines.Select(l => new CheckoutLineDto(
                l.ProductId,
                l.Quantity,
                l.UnitPrice,
                shown.Currency,
                products[l.ProductId].SellerId))
        ]);
    }

    public async Task StageClearAsync(Guid buyerId, CancellationToken cancellationToken)
    {
        var cart = await reader.FindAsync(buyerId, cancellationToken);

        cart?.Clear();
    }
}
