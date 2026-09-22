using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Shipping.Domain;

namespace UPBazaar.Modules.Shipping.Persistence.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments", ShippingModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One live consignment per part: two people packing at once cannot both book a courier.
        builder.HasIndex(x => x.OrderPartId)
            .IsUnique()
            .HasFilter("[Status] <> 'Cancelled'");

        // How courier updates find their shipment.
        builder.HasIndex(x => x.Awb).IsUnique().HasFilter("[Awb] IS NOT NULL");
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.OrderNumber);

        builder.Property(x => x.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Carrier).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CarrierReference).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PickupLocation).HasMaxLength(PickupLocation.NameMaxLength).IsRequired();
        builder.Property(x => x.PaymentMode).HasMaxLength(8).IsRequired();
        builder.Property(x => x.CarrierOrderId).HasMaxLength(32);
        builder.Property(x => x.CarrierShipmentId).HasMaxLength(32);
        builder.Property(x => x.Awb).HasMaxLength(40);
        builder.Property(x => x.CourierName).HasMaxLength(100);
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.Property(x => x.LengthCm).HasPrecision(6, 1);
        builder.Property(x => x.BreadthCm).HasPrecision(6, 1);
        builder.Property(x => x.HeightCm).HasPrecision(6, 1);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Ignore(x => x.IsLive);
        builder.Ignore(x => x.CanCancel);

        builder.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(x => x.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ShipmentEventConfiguration : IEntityTypeConfiguration<ShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentEvent> builder)
    {
        builder.ToTable("ShipmentEvents", ShippingModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.Property(x => x.Status).HasMaxLength(64).IsRequired();
    }
}

internal sealed class PickupLocationConfiguration : IEntityTypeConfiguration<PickupLocation>
{
    public void Configure(EntityTypeBuilder<PickupLocation> builder)
    {
        builder.ToTable("PickupLocations", ShippingModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One per seller, and one platform warehouse. EF would filter out NULLs from a unique
        // index on a nullable column; clearing the filter makes the NULL - the warehouse - unique too.
        builder.HasIndex(x => x.SellerId).IsUnique().HasFilter(null);

        builder.Property(x => x.Name).HasMaxLength(PickupLocation.NameMaxLength).IsRequired();
    }
}
