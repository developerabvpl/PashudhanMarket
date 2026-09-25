using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Settlements.Domain;

namespace UPBazaar.Modules.Settlements.Persistence.Configurations;

internal sealed class SettlementPolicyConfiguration : IEntityTypeConfiguration<SettlementPolicy>
{
    public void Configure(EntityTypeBuilder<SettlementPolicy> builder)
    {
        builder.ToTable("Policy", SettlementsModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.Property(x => x.DefaultCommissionPercent).HasPrecision(5, 2);
        builder.Property(x => x.TcsPercent).HasPrecision(5, 2);
        builder.Property(x => x.TdsPercent).HasPrecision(5, 2);
    }
}

internal sealed class SellerCommissionConfiguration : IEntityTypeConfiguration<SellerCommission>
{
    public void Configure(EntityTypeBuilder<SellerCommission> builder)
    {
        builder.ToTable("SellerCommissions", SettlementsModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.SellerId).IsUnique();

        builder.Property(x => x.CommissionPercent).HasPrecision(5, 2);
    }
}

internal sealed class EarningConfiguration : IEntityTypeConfiguration<Earning>
{
    public void Configure(EntityTypeBuilder<Earning> builder)
    {
        builder.ToTable("Earnings", SettlementsModule.SchemaName, table =>
            // A courier cost is the one kind that takes money away.
            table.HasCheckConstraint("CK_Earnings_NetAmount", "[NetAmount] >= 0 OR [Kind] = 'CourierCost'"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One sale and one delivery share per parcel, however many times its delivery is reported;
        // one courier cost per shipment charge. The filter is cleared so the NULL reference that
        // sales and delivery shares carry counts as a value, as it must for them to stay single.
        builder.HasIndex(x => new { x.OrderPartId, x.Kind, x.Reference }).IsUnique().HasFilter(null);

        // The payout run's scan, and a seller's statement.
        builder.HasIndex(x => new { x.Status, x.PayableFromUtc });
        builder.HasIndex(x => new { x.SellerId, x.DeliveredAtUtc });

        builder.Property(x => x.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(16);
        builder.Property(x => x.Reference).HasMaxLength(64);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.CommissionPercent).HasPrecision(5, 2);
        builder.Property(x => x.TcsPercent).HasPrecision(5, 2);
        builder.Property(x => x.TdsPercent).HasPrecision(5, 2);
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("Payouts", SettlementsModule.SchemaName, table =>
            table.HasCheckConstraint("CK_Payouts_NetAmount", "[NetAmount] >= 0"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // Finance's queue of transfers to make, oldest first.
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => x.SellerId);

        builder.Property(x => x.ShopName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AccountHolder).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AccountNumber).HasMaxLength(34).IsRequired();
        builder.Property(x => x.Ifsc).HasMaxLength(11).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Utr).HasMaxLength(32);
        builder.Property(x => x.PaidBy).HasMaxLength(64);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasMany(x => x.Earnings)
            .WithOne()
            .HasForeignKey(x => x.PayoutId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Earnings).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
