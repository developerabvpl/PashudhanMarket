using UPBazaar.Modules.Sellers.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;

namespace UPBazaar.Modules.Sellers.Application;

/// <summary>Maps sellers to the DTOs the API shows, masking the bank account.</summary>
internal static class SellerMappings
{
    public static SellerDto ToDto(this Seller seller) => new(
        seller.PublicId,
        seller.OwnerUserId,
        seller.Status.ToString(),
        seller.ShopName,
        seller.Description,
        seller.ContactMobile,
        seller.ContactEmail,
        new SellerAddressDto(seller.AddressLine1, seller.AddressLine2, seller.City, seller.State, seller.Pincode),
        new SellerKycDto(
            seller.LegalName,
            seller.Gstin,
            seller.Pan,
            seller.BankAccountHolder,
            seller.BankAccountNumber.Length >= 4 ? seller.BankAccountNumber[^4..] : seller.BankAccountNumber,
            seller.Ifsc),
        seller.ReviewNote,
        seller.SubmittedAtUtc,
        seller.ReviewedAtUtc);
}
