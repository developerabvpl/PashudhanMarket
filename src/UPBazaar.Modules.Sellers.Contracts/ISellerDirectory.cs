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
}

/// <summary>The one failure every seller-facing endpoint shares.</summary>
public static class SellerAccessErrors
{
    public static readonly Error NotAnApprovedSeller = Error.Forbidden(
        "sellers.not_approved",
        "Only an approved seller can do this. Finish your seller application first.");
}
