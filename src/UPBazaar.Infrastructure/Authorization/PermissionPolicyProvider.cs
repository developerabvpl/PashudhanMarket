using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace UPBazaar.Infrastructure.Authorization;

/// <summary>
/// Turns any [Authorize("some.permission")] into a policy on demand, so adding an endpoint
/// never means editing a central policy list.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var configured = await base.GetPolicyAsync(policyName);

        if (configured is not null)
        {
            return configured;
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}
