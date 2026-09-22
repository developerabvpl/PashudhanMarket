using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Sellers.Domain;

namespace UPBazaar.Modules.Sellers.Persistence.Configurations;

internal sealed class SellerConfiguration : IEntityTypeConfiguration<Seller>
{
    public void Configure(EntityTypeBuilder<Seller> builder)
    {
        builder.ToTable("Sellers", SellersModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One shop per account, and how every seller-facing request finds its seller.
        builder.HasIndex(x => x.OwnerUserId).IsUnique().HasFilter("[OwnerUserId] IS NOT NULL");

        // The review queue.
        builder.HasIndex(x => new { x.Status, x.SubmittedAtUtc });

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ShopName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.ContactMobile).HasMaxLength(10).IsRequired();
        builder.Property(x => x.AddressLine1).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AddressLine2).HasMaxLength(200);
        builder.Property(x => x.City).HasMaxLength(100).IsRequired();
        builder.Property(x => x.State).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Pincode).HasMaxLength(6).IsRequired();
        builder.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Gstin).HasMaxLength(15);
        builder.Property(x => x.Pan).HasMaxLength(10).IsRequired();
        builder.Property(x => x.BankAccountHolder).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BankAccountNumber).HasMaxLength(18).IsRequired();
        builder.Property(x => x.Ifsc).HasMaxLength(11).IsRequired();
        builder.Property(x => x.ReviewNote).HasMaxLength(1000);
        builder.Property(x => x.ReviewedBy).HasMaxLength(64);
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
