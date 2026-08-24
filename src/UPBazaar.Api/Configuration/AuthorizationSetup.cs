using Microsoft.AspNetCore.Authorization;
using UPBazaar.Infrastructure.Identity;
using UPBazaar.Modules.Identity.Services;

namespace UPBazaar.Api.Configuration;

/// <summary>Turns the permission catalogue into authorization policies.</summary>
public static class AuthorizationSetup
{
    /// <summary>
    /// Registers one policy per catalogued permission, plus a deny-by-default fallback.
    ///
    /// Generating them means <c>[Authorize("catalog.products.write")]</c> works the moment the
    /// permission is added to the catalogue, and — more usefully — a typo in an attribute
    /// fails closed rather than matching nothing and being ignored.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPermissionPolicies(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        foreach (var permission in PermissionCatalog.All)
        {
            builder.AddPolicy(permission, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(CurrentUser.PermissionClaimType, permission));
        }

        // The jobs dashboard names its permission separately so the Hangfire wiring does not
        // have to reach into the Identity module.
        builder.AddPolicy(HangfireSetup.DashboardPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(CurrentUser.PermissionClaimType, HangfireSetup.DashboardPermission));

        return services;
    }
}
