using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Shipping.Domain;

namespace UPBazaar.Modules.Shipping.Persistence.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipment", ShippingModuleSchema.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => x.AwbNumber).IsUnique().HasFilter("[AwbNumber] IS NOT NULL");

        builder.Property(x => x.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DeliveryPostcode).HasMaxLength(12).IsRequired();
        builder.Property(x => x.AwbNumber).HasMaxLength(64);
        builder.Property(x => x.Courier).HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Ignore(x => x.DomainEvents);
    }
}
