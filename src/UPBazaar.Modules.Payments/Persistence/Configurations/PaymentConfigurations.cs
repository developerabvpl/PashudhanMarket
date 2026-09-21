using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Payments.Domain;

namespace UPBazaar.Modules.Payments.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", PaymentsModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // How the browser and webhooks find a payment; one of ours per gateway order.
        builder.HasIndex(x => x.GatewayOrderId).IsUnique();
        builder.HasIndex(x => x.GatewayPaymentId);
        builder.HasIndex(x => x.OrderId);

        // The settlement job's scan for money not yet applied to an order.
        builder.HasIndex(x => new { x.Status, x.OrderOutcome, x.PaidAtUtc });

        builder.Property(x => x.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.OrderOutcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.OrderOutcomeReason).HasMaxLength(500);
        builder.Property(x => x.Gateway).HasMaxLength(16).IsRequired();
        builder.Property(x => x.GatewayOrderId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.GatewayPaymentId).HasMaxLength(64);
        builder.Property(x => x.LastFailure).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Ignore(x => x.RefundDue);

        builder.HasMany(x => x.Refunds)
            .WithOne()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds", PaymentsModule.SchemaName, table =>
            table.HasCheckConstraint("CK_Refunds_Amount", "[Amount] > 0"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // The finance queue: what is still owed, oldest first.
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });

        // One refund per cancelled part, even if the cancellation event arrives twice.
        builder.HasIndex(x => new { x.PaymentId, x.OrderPartId })
            .IsUnique()
            .HasFilter("[OrderPartId] IS NOT NULL");

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.GatewayRefundId).HasMaxLength(64);
        builder.Property(x => x.RefundedBy).HasMaxLength(64);
    }
}

internal sealed class ProcessedWebhookEventConfiguration : IEntityTypeConfiguration<ProcessedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookEvent> builder)
    {
        builder.ToTable("ProcessedWebhookEvents", PaymentsModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.EventId).IsUnique();

        builder.Property(x => x.EventId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
    }
}
