namespace UPBazaar.Infrastructure.Authorization;

public static class PermissionClaims
{
    /// <summary>Claim type carrying a single granted permission, e.g. catalog.products.write.</summary>
    public const string ClaimType = "permission";
}
