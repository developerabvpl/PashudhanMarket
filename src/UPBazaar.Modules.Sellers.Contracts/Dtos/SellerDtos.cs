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
    DateTime? ReviewedAtUtc);

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
