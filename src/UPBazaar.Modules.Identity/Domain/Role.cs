using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Identity.Domain;

/// <summary>
/// A named bundle of permissions. Users hold roles; endpoints demand permissions. The
/// indirection is what lets an administrator widen a job function without a deployment.
/// </summary>
public sealed class Role : Entity, IAuditable
{
    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>Seeded roles cannot be deleted; deleting SuperAdmin would strand the platform.</summary>
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public static Role Create(string name, string? description, bool isSystem) => new()
    {
        Name = name,
        Description = description,
        IsSystem = isSystem,
    };

    public void Grant(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        if (_permissions.Any(p => p.PermissionId == permission.Id))
        {
            return;
        }

        _permissions.Add(RolePermission.Create(permission));
    }

    public void Revoke(long permissionId)
    {
        var existing = _permissions.FirstOrDefault(p => p.PermissionId == permissionId);

        if (existing is not null)
        {
            _permissions.Remove(existing);
        }
    }
}
