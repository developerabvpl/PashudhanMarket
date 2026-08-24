namespace UPBazaar.Modules.Identity.Domain;

/// <summary>
/// Join row holding one role for one user, with who granted it and when. Role assignment is
/// the most privilege-relevant change an administrator can make, so it records its own
/// provenance rather than relying only on the audit log.
/// </summary>
public sealed class UserRole
{
    private UserRole()
    {
    }

    public long UserId { get; private set; }

    public long RoleId { get; private set; }

    public Role Role { get; private set; } = null!;

    public DateTime AssignedAtUtc { get; private set; }

    public string? AssignedBy { get; private set; }

    internal static UserRole Create(Role role, DateTime utcNow, string? assignedBy) => new()
    {
        RoleId = role.Id,
        Role = role,
        AssignedAtUtc = utcNow,
        AssignedBy = assignedBy,
    };
}
