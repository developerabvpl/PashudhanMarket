namespace UPBazaar.Modules.Settlements.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
/// </summary>
public static class SettlementsPermissions
{
    /// <summary>See every seller's earnings and payouts, with the bank account each is paid to.</summary>
    public const string Read = "settlements.read";

    /// <summary>Run payouts and record them as paid: the step that sends money out.</summary>
    public const string PayoutsApprove = "settlements.payouts.approve";

    /// <summary>
    /// Change the commission and tax rates. Separate from paying out, because a rate change moves
    /// every future payout, not just one.
    /// </summary>
    public const string PolicyWrite = "settlements.policy.write";

    /// <summary>A seller sees their own earnings and payouts.</summary>
    public const string OwnRead = "settlements.own.read";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Read,
        PayoutsApprove,
        PolicyWrite,
        OwnRead,
    ];
}
