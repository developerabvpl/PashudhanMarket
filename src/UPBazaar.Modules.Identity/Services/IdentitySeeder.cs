using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Modules.Identity.Services;

/// <summary>
/// Brings the database in line with the permission catalogue on startup, and creates the first
/// administrator when one is configured and absent.
///
/// Written to be idempotent: it runs on every boot, adds what is missing, and leaves alone
/// what is already there. Permissions and role grants are reconciled so that adding a
/// permission to a seed role in code takes effect on the next deployment; a permission removed
/// from the catalogue is left in the database rather than deleted, because dropping a row that
/// a custom role still references would silently widen or narrow somebody's access.
/// </summary>
public sealed class IdentitySeeder(
    UPBazaarDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    IOptions<IdentityModuleOptions> options,
    IClock clock,
    ILogger<IdentitySeeder> logger)
{
    private readonly IdentityModuleOptions _options = options.Value;

    /// <summary>Runs the whole seed. Safe to call repeatedly.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await SeedPermissionsAsync(cancellationToken);
        await SeedRolesAsync(permissions, cancellationToken);
        await SeedSuperAdminAsync(cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<string, Permission>> SeedPermissionsAsync(
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<Permission>()
            .ToDictionaryAsync(p => p.Name, StringComparer.Ordinal, cancellationToken);

        foreach (var name in PermissionCatalog.All)
        {
            if (existing.ContainsKey(name))
            {
                continue;
            }

            var permission = Permission.Create(name);
            dbContext.Set<Permission>().Add(permission);
            existing[name] = permission;

            SeedLog.PermissionSeeded(logger, name);
        }

        // Saved here so role grants can reference real ids rather than temporary ones.
        await dbContext.SaveChangesAsync(cancellationToken);

        return existing;
    }

    private async Task SeedRolesAsync(
        Dictionary<string, Permission> permissions,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<Role>()
            .Include(r => r.Permissions)
            .ToDictionaryAsync(r => r.Name, StringComparer.Ordinal, cancellationToken);

        foreach (var definition in PermissionCatalog.Roles)
        {
            if (!existing.TryGetValue(definition.Name, out var role))
            {
                role = Role.Create(definition.Name, definition.Description, isSystem: true);
                dbContext.Set<Role>().Add(role);
                existing[definition.Name] = role;

                SeedLog.RoleSeeded(logger, definition.Name);
            }

            foreach (var permissionName in definition.Permissions)
            {
                if (permissions.TryGetValue(permissionName, out var permission))
                {
                    role.Grant(permission);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSuperAdminAsync(CancellationToken cancellationToken)
    {
        var seed = _options.SuperAdmin;

        if (!seed.IsConfigured)
        {
            SeedLog.NoSuperAdminConfigured(logger);

            return;
        }

        var email = User.Normalize(seed.Email!);

        if (await dbContext.Set<User>().AnyAsync(u => u.Email == email, cancellationToken))
        {
            return;
        }

        var user = User.CreateWithPassword(
            UserType.Staff,
            email,
            passwordHash: string.Empty,
            seed.DisplayName,
            language: "en");

        user.SetPassword(passwordHasher.HashPassword(user, seed.Password!));
        user.ConfirmEmail();

        var superAdmin = await dbContext.Set<Role>()
            .FirstAsync(r => r.Name == PermissionCatalog.RoleNames.SuperAdmin, cancellationToken);

        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        user.AssignRole(superAdmin, clock.UtcNow, assignedBy: "seed");

        SeedLog.SuperAdminSeeded(logger, email);
    }
}

/// <summary>
/// Source-generated log messages for seeding.
///
/// The generator produces a strongly-typed method per message that checks the level before
/// touching its arguments, which is what CA1873 asks for and what keeps a disabled log level
/// genuinely free.
/// </summary>
internal static partial class SeedLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded permission {Permission}")]
    public static partial void PermissionSeeded(ILogger logger, string permission);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded role {Role}")]
    public static partial void RoleSeeded(ILogger logger, string role);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No SuperAdmin seed configured; set Identity:SuperAdmin:Email and :Password to create one.")]
    public static partial void NoSuperAdminConfigured(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Seeded SuperAdmin {Email}. Change this password immediately after first sign-in.")]
    public static partial void SuperAdminSeeded(ILogger logger, string email);
}
