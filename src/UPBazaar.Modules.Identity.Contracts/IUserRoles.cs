using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Contracts;

/// <summary>
/// Role changes other modules may make as a consequence of their own decisions - Sellers
/// granting SellerOwner when a shop is approved. Only named system roles; staff roles stay the
/// business of the identity admin screens.
/// </summary>
public interface IUserRoles
{
    /// <summary>
    /// Grants a role. Staged, not saved: the caller saves it in the same transaction as the
    /// decision that justifies it, so an approval never lands without the access it grants.
    /// Granting a role the user already holds is a no-op. The user's current access token does
    /// not change; the grant shows up when it is next refreshed.
    /// </summary>
    Task<Result> StageGrantAsync(Guid userId, string roleName, string grantedBy, CancellationToken cancellationToken);
}
