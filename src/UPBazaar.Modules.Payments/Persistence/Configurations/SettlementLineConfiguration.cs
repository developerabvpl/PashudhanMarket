using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Payments.Domain;

namespace UPBazaar.Modules.Payments.Persistence.Configurations;

internal sealed class SettlementLineConfiguration : IEntityTypeConfiguration<SettlementLine>
{
    public void Configure(EntityTypeBuilder<SettlementLine> builder)
    {
        builder.ToTable("SettlementLine", PaymentsModuleSchema.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => new { x.SellerId, x.Status });

        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(x => x.GrossAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.CommissionAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.NetAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Ignore(x => x.DomainEvents);
    }
}
