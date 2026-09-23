using UPBazaar.Modules.Sellers.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Sellers.Contracts;

/// <summary>
/// Which seller a signed-in user acts for. Every seller-facing endpoint in Catalog, Inventory,
/// Orders and Shipping starts here, so a seller's products, orders and parcels are found from
/// their token and never from an id they could edit in a request.
/// </summary>
public interface ISellerDirectory
{
    /// <summary>
    /// The approved seller this user owns, or null. Null too while the application is pending or
    /// after it was rejected: an unapproved seller may not act as one anywhere.
    /// </summary>
    Task<Guid?> GetApprovedSellerIdAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Shop names for the given sellers, whatever their status, so staff screens can show a name
    /// where a record only carries an id. Ids that match no seller are left out of the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetShopNamesAsync(
        IReadOnlyCollection<Guid> sellerIds,
        CancellationToken cancellationToken);

    /// <summary>Every approved seller, by shop name: the ones staff may list products for.</summary>
    Task<IReadOnlyList<SellerNameDto>> ListApprovedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Where a buyer's return to this seller goes: their registered business address, since the
    /// seller inspects what comes back. Null if the id matches no seller.
    /// </summary>
    Task<SellerReturnAddressDto?> GetReturnAddressAsync(Guid sellerId, CancellationToken cancellationToken);
}

/// <summary>The one failure every seller-facing endpoint shares.</summary>
public static class SellerAccessErrors
{
    public static readonly Error NotAnApprovedSeller = Error.Forbidden(
        "sellers.not_approved",
        "Only an approved seller can do this. Finish your seller application first.");
}
