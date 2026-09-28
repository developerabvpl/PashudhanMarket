using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Sellers.Domain;

/// <summary>Every failure this module can return.</summary>
public static class SellerErrors
{
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "sellers.not_signed_in",
        "Sign in to apply as a seller.");

    public static readonly Error NotFound = Error.NotFound(
        "sellers.not_found",
        "Seller not found.");

    public static readonly Error NoApplication = Error.NotFound(
        "sellers.no_application",
        "You have not applied to sell yet.");

    public static readonly Error AlreadyApplied = Error.Conflict(
        "sellers.already_applied",
        "You have already applied. Edit your application instead.");

    public static readonly Error AlreadyApproved = Error.Conflict(
        "sellers.already_approved",
        "Your shop is approved. Legal and payout details can only be changed by support.");

    public static readonly Error NotPending = Error.Conflict(
        "sellers.not_pending",
        "Only an application waiting for review can be approved or rejected.");

    public static readonly Error AlreadyOwned = Error.Conflict(
        "sellers.already_owned",
        "This seller already has an owner.");

    public static readonly Error OwnerHasShop = Error.Conflict(
        "sellers.owner_has_shop",
        "That account already runs a shop. One account, one shop.");

    public static readonly Error NoSellerAccount = Error.NotFound(
        "sellers.team.no_account",
        "No account uses that email. Ask them to register on the seller portal first.");

    public static readonly Error NotYourself = Error.Validation(
        "sellers.team.not_yourself",
        "You own the shop already. Add someone else.");

    public static readonly Error AlreadyInATeam = Error.Conflict(
        "sellers.team.already_in_a_team",
        "That account already runs a shop or works for one. One account, one shop.");

    public static readonly Error TeamFull = Error.Conflict(
        "sellers.team.full",
        "A shop can have up to 20 team members. Remove someone first.");

    public static readonly Error MemberNotFound = Error.NotFound(
        "sellers.team.member_not_found",
        "That person is not on your team.");

    public static readonly Error NotApprovedShop = Error.Conflict(
        "sellers.team.not_approved",
        "Your shop needs to be approved before you add a team.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "sellers.concurrent_change",
        "The seller was changed by someone else at the same time. Reload and try again.");
}
