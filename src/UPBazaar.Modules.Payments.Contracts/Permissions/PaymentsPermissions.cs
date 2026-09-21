namespace UPBazaar.Modules.Payments.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
/// </summary>
public static class PaymentsPermissions
{
    /// <summary>Pay for your own orders. Held by buyers.</summary>
    public const string OwnWrite = "payments.own.write";

    /// <summary>See every payment and refund, with the gateway's ids, for support and finance.</summary>
    public const string Read = "payments.read";

    /// <summary>
    /// Record that a refund has been made. Refunds are made by hand in the Razorpay dashboard for
    /// now; this is the permission to say so, and it moves money on paper, so it is kept narrow.
    /// </summary>
    public const string RefundsWrite = "payments.refunds.write";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        OwnWrite,
        Read,
        RefundsWrite,
    ];
}
