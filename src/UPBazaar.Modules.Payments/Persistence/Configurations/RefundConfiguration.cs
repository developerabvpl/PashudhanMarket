using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Payments.Domain;

namespace UPBazaar.Modules.Payments.Persistence.Configurations;

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refund", PaymentsModuleSchema.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.GatewayRefundId).IsUnique();

        builder.Property(x => x.GatewayRefundId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Ignore(x => x.DomainEvents);
    }
}
