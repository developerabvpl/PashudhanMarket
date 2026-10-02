namespace UPBazaar.Modules.Sellers.Contracts.Dtos;

/// <summary>
/// A seller's shop and application, as its owner and staff see it.
///
/// The bank account number is never sent back in full - only its last four digits - because a
/// screen that shows it adds nothing and a leaked response would give away where payouts go.
/// </summary>
/// <param name="Id">Public id: the seller id products, orders and shipments carry.</param>
/// <param name="OwnerUserId">The account that runs it, or null for a seller set up without one.</param>
/// <param name="Status">Pending (waiting for review), Approved, or Rejected (see ReviewNote; the owner may correct and resubmit).</param>
/// <param name="ShopName">Name buyers see.</param>
/// <param name="Description">About the shop, if given.</param>
/// <param name="ContactMobile">For couriers and support.</param>
/// <param name="ContactEmail">For support, if given.</param>
/// <param name="Address">Registered business address.</param>
/// <param name="Kyc">Legal and payout details.</param>
/// <param name="ReviewNote">Why it was rejected, when it was.</param>
/// <param name="SubmittedAtUtc">When the application was last submitted.</param>
/// <param name="ReviewedAtUtc">When it was last approved or rejected.</param>
/// <param name="OwnerName">
/// The owner's display name, for staff who know people by name rather than by account id. Filled
/// in only on the staff view of one seller; null elsewhere and when there is no owner.
/// </param>
/// <param name="OwnerEmail">The owner's email address, on the same staff view, if their account has one.</param>
public sealed record SellerDto(
    Guid Id,
    Guid? OwnerUserId,
    string Status,
    string ShopName,
    string? Description,
    string ContactMobile,
    string? ContactEmail,
    SellerAddressDto Address,
    SellerKycDto Kyc,
    string? ReviewNote,
    DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? OwnerName = null,
    string? OwnerEmail = null);

/// <summary>A registered business address.</summary>
public sealed record SellerAddressDto(string Line1, string? Line2, string City, string State, string Pincode);

/// <summary>Legal and payout details, with the account number masked.</summary>
/// <param name="LegalName">Name as registered with the tax authorities.</param>
/// <param name="Gstin">GST number, if registered for GST; small sellers under the threshold need not be.</param>
/// <param name="Pan">PAN of the business or proprietor.</param>
/// <param name="BankAccountHolder">Name on the bank account.</param>
/// <param name="BankAccountLast4">Last four digits of the account number.</param>
/// <param name="Ifsc">Bank branch IFSC.</param>
public sealed record SellerKycDto(
    string LegalName,
    string? Gstin,
    string Pan,
    string BankAccountHolder,
    string BankAccountLast4,
    string Ifsc);

/// <summary>A seller in a list: enough to recognise and review one.</summary>
public sealed record SellerSummaryDto(
    Guid Id,
    string ShopName,
    string Status,
    string ContactMobile,
    Guid? OwnerUserId,
    DateTime SubmittedAtUtc);

/// <summary>A seller by name only, for pickers and for labelling other modules' records.</summary>
/// <param name="Id">Public id: the seller id products, orders and shipments carry.</param>
/// <param name="ShopName">Name buyers see.</param>
public sealed record SellerNameDto(Guid Id, string ShopName);

/// <summary>Where a buyer's return is taken, and who to hand it to.</summary>
/// <param name="ShopName">Name the courier asks for.</param>
/// <param name="ContactMobile">For the courier.</param>
/// <param name="Address">The seller's registered business address.</param>
public sealed record SellerReturnAddressDto(string ShopName, string ContactMobile, SellerAddressDto Address);

/// <summary>
/// Where a seller's payouts are sent, in full. Only Settlements asks for it, for the finance staff
/// who make the transfer; everything else sees the account number masked.
/// </summary>
/// <param name="ShopName">Name the payout is for.</param>
/// <param name="AccountHolder">Name on the bank account.</param>
/// <param name="AccountNumber">The full account number.</param>
/// <param name="Ifsc">The branch's IFSC.</param>
public sealed record SellerPayoutAccountDto(string ShopName, string AccountHolder, string AccountNumber, string Ifsc);

/// <summary>Where to write to a seller.</summary>
/// <param name="ShopName">Name to address them by.</param>
/// <param name="Email">
/// The shop's contact email, or failing that its owner's sign-in email; null when it has neither.
/// </param>
public sealed record SellerContactDto(string ShopName, string? Email);

/// <summary>Which shop the caller works for, and as what: what the seller portal is built around.</summary>
/// <param name="SellerId">The shop.</param>
/// <param name="ShopName">Its name.</param>
/// <param name="Status">Pending, Approved or Rejected. Only an approved shop can be worked for.</param>
/// <param name="Role">Owner, Manager or Dispatch.</param>
public sealed record SellerAccessDto(Guid SellerId, string ShopName, string Status, string Role);

/// <summary>Someone on a shop's team.</summary>
/// <param name="Id">Public id of the membership.</param>
/// <param name="UserId">Their account.</param>
/// <param name="DisplayName">Their name, as they gave it.</param>
/// <param name="Email">The email they sign in with.</param>
/// <param name="Role">Manager or Dispatch.</param>
/// <param name="AddedAtUtc">When the owner added them.</param>
public sealed record SellerMemberDto(Guid Id, Guid UserId, string DisplayName, string? Email, string Role, DateTime AddedAtUtc);
