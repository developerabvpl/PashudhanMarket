using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Promotions.Domain;

namespace UPBazaar.Modules.Promotions.Persistence.Configurations;

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("Coupons", PromotionsModule.SchemaName, table =>
        {
            table.HasCheckConstraint(
                "CK_Coupons_Value",
                "([DiscountType] = 'FreeDelivery' AND [Value] = 0) OR ([DiscountType] <> 'FreeDelivery' AND [Value] > 0 AND ([DiscountType] <> 'Percent' OR [Value] <= 100))");
            table.HasCheckConstraint("CK_Coupons_Uses", "[Uses] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // A code means one coupon, whoever runs it.
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.SellerId);

        builder.Property(x => x.Code).HasMaxLength(Coupon.CodeMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Coupon.DescriptionMaxLength).IsRequired();
        builder.Property(x => x.FundedBy).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Ignore(x => x.IsCampaign);

        builder.HasMany(x => x.Sellers)
            .WithOne()
            .HasForeignKey(x => x.CouponId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Sellers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CouponSellerConfiguration : IEntityTypeConfiguration<CouponSeller>
{
    public void Configure(EntityTypeBuilder<CouponSeller> builder)
    {
        builder.ToTable("CouponSellers", PromotionsModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => new { x.CouponId, x.SellerId }).IsUnique();
    }
}

internal sealed class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ToTable("CouponRedemptions", PromotionsModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One coupon per order.
        builder.HasIndex(x => x.OrderId).IsUnique();

        // A buyer's uses of a coupon.
        builder.HasIndex(x => new { x.CouponId, x.BuyerId });

        builder.HasOne<Coupon>()
            .WithMany()
            .HasForeignKey(x => x.CouponId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
