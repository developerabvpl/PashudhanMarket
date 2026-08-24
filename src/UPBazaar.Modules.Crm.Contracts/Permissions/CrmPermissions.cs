namespace UPBazaar.Modules.Crm.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
/// </summary>
public static class CrmPermissions
{
    public const string CustomersRead = "crm.customers.read";

    public const string TicketsWrite = "crm.tickets.write";
}
