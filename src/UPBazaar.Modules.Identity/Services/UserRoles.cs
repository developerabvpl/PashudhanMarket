using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Contracts;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Services;

/// <summary>Implements <see cref="IUserRoles"/>.</summary>
internal sealed class UserRoles(UPBazaarDbContext dbContext, IClock clock) : IUserRoles
{
    /// <summary>
    /// The roles another module may hand out. A module granting Admin because its own logic said
    /// so would be an escalation path no one reviewed, so the list is short and explicit.
    /// </summary>
    private static readonly string[] Grantable = [PermissionCatalog.RoleNames.SellerOwner];

    public async Task<Result> StageGrantAsync(
        Guid userId,
        string roleName,
        string grantedBy,
        CancellationToken cancellationToken)
    {
        if (!Grantable.Contains(roleName, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Role '{roleName}' cannot be granted by another module.", nameof(roleName));
        }

        var user = await dbContext.Set<User>()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.PublicId == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        // Seeded at every start-up; missing means the seed has not run, which is a deployment fault.
        var role = await dbContext.Set<Role>().FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{roleName}' does not exist.");

        user.AssignRole(role, clock.UtcNow, grantedBy);

        return Result.Success();
    }
}
