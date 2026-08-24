namespace UPBazaar.Modules.Identity.Domain;

/// <summary>Join row granting one permission to one role.</summary>
public sealed class RolePermission
{
    private RolePermission()
    {
    }

    public long RoleId { get; private set; }

    public long PermissionId { get; private set; }

    public Permission Permission { get; private set; } = null!;

    internal static RolePermission Create(Permission permission) => new()
    {
        PermissionId = permission.Id,
        Permission = permission,
    };
}
