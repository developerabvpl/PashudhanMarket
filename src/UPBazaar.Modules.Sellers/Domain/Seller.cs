using UPBazaar.Modules.Sellers.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Sellers.Domain;

/// <summary>Where a seller's application stands. Stored by name.</summary>
public enum SellerStatus
{
    /// <summary>Submitted and waiting for a reviewer.</summary>
    Pending = 0,

    /// <summary>May list products and fulfil orders.</summary>
    Approved = 1,

    /// <summary>Turned down, with a note saying why. The owner can correct it and submit again.</summary>
    Rejected = 2,
}

/// <summary>What an application says, as the applicant entered it.</summary>
public sealed record SellerApplication(
    string ShopName,
    string? Description,
    string ContactMobile,
    string? ContactEmail,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string Pincode,
    string LegalName,
    string? Gstin,
    string Pan,
    string BankAccountHolder,
    string BankAccountNumber,
    string Ifsc);

/// <summary>
/// A shop on the marketplace, and the application behind it.
///
/// Its public id is the seller id Catalog, Orders and Shipping already carry, so approving an
/// application changes nothing in those modules - the id was always the seller's; now it has an
/// owner who may act for it.
///
/// A seller sells only once approved. Until then the application can be edited and resubmitted as
/// often as the applicant likes; afterwards the legal and payout details are frozen, because they
/// are what the approval vouched for, while the shop's name and contact details stay editable.
/// </summary>
public sealed class Seller : AggregateRoot, IAuditable
{
    private Seller()
    {
    }

    /// <summary>Identity's id for the owner, or null for a seller set up by staff without one.</summary>
    public Guid? OwnerUserId { get; private set; }

    public SellerStatus Status { get; private set; }

    public string ShopName { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string ContactMobile { get; private set; } = string.Empty;

    public string? ContactEmail { get; private set; }

    public string AddressLine1 { get; private set; } = string.Empty;

    public string? AddressLine2 { get; private set; }

    public string City { get; private set; } = string.Empty;

    public string State { get; private set; } = string.Empty;

    public string Pincode { get; private set; } = string.Empty;

    public string LegalName { get; private set; } = string.Empty;

    public string? Gstin { get; private set; }

    public string Pan { get; private set; } = string.Empty;

    public string BankAccountHolder { get; private set; } = string.Empty;

    /// <summary>
    /// In full, because payouts will need it. Stored as plain text for now, like the TOTP secrets;
    /// it should be encrypted at rest before real payouts run.
    /// </summary>
    public string BankAccountNumber { get; private set; } = string.Empty;

    public string Ifsc { get; private set; } = string.Empty;

    public string? ReviewNote { get; private set; }

    public DateTime SubmittedAtUtc { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    public string? ReviewedBy { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public static Seller Apply(Guid ownerUserId, SellerApplication application, DateTime now)
    {
        var seller = new Seller { OwnerUserId = ownerUserId };

        seller.Fill(application);
        seller.Status = SellerStatus.Pending;
        seller.SubmittedAtUtc = now;

        return seller;
    }

    /// <summary>
    /// A seller that already exists by id - the one the sample catalogue was imported under - set
    /// up approved, with no owner until staff link one.
    /// </summary>
    public static Seller Seed(Guid id, SellerApplication application, DateTime now)
    {
        var seller = new Seller { PublicId = id };

        seller.Fill(application);
        seller.Status = SellerStatus.Approved;
        seller.SubmittedAtUtc = now;
        seller.ReviewedAtUtc = now;
        seller.ReviewedBy = "seed";

        return seller;
    }

    /// <summary>Corrects an application and puts it back in the queue. Refused once approved.</summary>
    public Result Resubmit(SellerApplication application, DateTime now)
    {
        if (Status == SellerStatus.Approved)
        {
            return Result.Failure(SellerErrors.AlreadyApproved);
        }

        Fill(application);
        Status = SellerStatus.Pending;
        ReviewNote = null;
        SubmittedAtUtc = now;

        return Result.Success();
    }

    /// <summary>What an approved shop may still change about itself: how it looks and how to reach it.</summary>
    public void UpdateProfile(string shopName, string? description, string contactMobile, string? contactEmail)
    {
        ShopName = shopName.Trim();
        Description = Blank(description);
        ContactMobile = contactMobile.Trim();
        ContactEmail = Blank(contactEmail);
    }

    public Result Approve(string reviewedBy, DateTime now)
    {
        if (Status != SellerStatus.Pending)
        {
            return Result.Failure(SellerErrors.NotPending);
        }

        Status = SellerStatus.Approved;
        ReviewNote = null;
        ReviewedAtUtc = now;
        ReviewedBy = reviewedBy;

        Raise(new SellerApprovedDomainEvent(PublicId, ShopName));

        return Result.Success();
    }

    public Result Reject(string note, string reviewedBy, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        if (Status != SellerStatus.Pending)
        {
            return Result.Failure(SellerErrors.NotPending);
        }

        Status = SellerStatus.Rejected;
        ReviewNote = note.Trim();
        ReviewedAtUtc = now;
        ReviewedBy = reviewedBy;

        Raise(new SellerRejectedDomainEvent(PublicId, ShopName, ReviewNote));

        return Result.Success();
    }

    /// <summary>Gives an ownerless seller its owner. A seller already owned keeps its owner.</summary>
    public Result LinkOwner(Guid ownerUserId)
    {
        if (OwnerUserId is not null)
        {
            return Result.Failure(SellerErrors.AlreadyOwned);
        }

        OwnerUserId = ownerUserId;

        return Result.Success();
    }

    private void Fill(SellerApplication a)
    {
        UpdateProfile(a.ShopName, a.Description, a.ContactMobile, a.ContactEmail);
        AddressLine1 = a.AddressLine1.Trim();
        AddressLine2 = Blank(a.AddressLine2);
        City = a.City.Trim();
        State = a.State.Trim();
        Pincode = a.Pincode.Trim();
        LegalName = a.LegalName.Trim();
        Gstin = Blank(a.Gstin)?.ToUpperInvariant();
        Pan = a.Pan.Trim().ToUpperInvariant();
        BankAccountHolder = a.BankAccountHolder.Trim();
        BankAccountNumber = a.BankAccountNumber.Trim();
        Ifsc = a.Ifsc.Trim().ToUpperInvariant();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
