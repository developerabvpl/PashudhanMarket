namespace UPBazaar.Modules.Identity.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and because
/// the policy provider turns any string here into a policy on demand - this list is the whole
/// registration.
/// </summary>
public static class IdentityPermissions
{
    /// <summary>Read staff and customer accounts.</summary>
    public const string UsersRead = "identity.users.read";

    /// <summary>Create, update and deactivate accounts, and assign roles.</summary>
    public const string UsersManage = "identity.users.manage";

    /// <summary>Read the role and permission catalogue.</summary>
    public const string RolesRead = "identity.roles.read";

    /// <summary>Change which permissions a role grants.</summary>
    public const string RolesWrite = "identity.roles.write";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        UsersRead,
        UsersManage,
        RolesRead,
        RolesWrite,
    ];
}
