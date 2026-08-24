using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Payments.Domain;

namespace UPBazaar.Modules.Payments.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payment", PaymentsModuleSchema.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.GatewayOrderId).IsUnique();
        builder.HasIndex(x => x.GatewayPaymentId)
            .IsUnique()
            .HasFilter("[GatewayPaymentId] IS NOT NULL");

        builder.Property(x => x.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.GatewayOrderId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.GatewayPaymentId).HasMaxLength(128);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(512);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.RefundedAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasMany(x => x.Refunds)
            .WithOne()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.SettlementLines)
            .WithOne()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.SettlementLines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.RefundableAmount);
        builder.Ignore(x => x.DomainEvents);
    }
}
