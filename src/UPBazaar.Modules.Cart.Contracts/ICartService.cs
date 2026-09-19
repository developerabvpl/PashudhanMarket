using UPBazaar.Modules.Cart.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Cart.Contracts;

/// <summary>What checkout, in the Orders module, needs from a cart.</summary>
public interface ICartService
{
    /// <summary>
    /// The buyer's lines, ready to order. Fails with a conflict if any line has a problem - a
    /// changed price, a withdrawn product, too little stock - because checkout must never charge
    /// a buyer for something other than what their cart showed them.
    /// </summary>
    Task<Result<IReadOnlyList<CheckoutLineDto>>> GetCheckoutLinesAsync(
        Guid buyerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Empties the cart once an order is placed. Staged, not saved: the caller saves it in the
    /// same transaction as the order, so a placed order never leaves a full cart behind.
    /// </summary>
    Task StageClearAsync(Guid buyerId, CancellationToken cancellationToken);
}
