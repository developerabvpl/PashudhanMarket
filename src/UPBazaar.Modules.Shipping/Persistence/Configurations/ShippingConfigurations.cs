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

        // One live consignment per part each way: two people packing at once cannot both book a
        // courier, and a return approved twice cannot book two pickups.
        builder.HasIndex(x => new { x.OrderPartId, x.Direction })
            .IsUnique()
            .HasFilter("[Status] <> 'Cancelled'");

        // How courier updates find their shipment.
        builder.HasIndex(x => x.Awb).IsUnique().HasFilter("[Awb] IS NOT NULL");
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.OrderNumber);

        builder.Property(x => x.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(x => x.Carrier).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CarrierReference).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PickupLocation).HasMaxLength(PickupLocation.NameMaxLength).IsRequired();
        builder.Property(x => x.PaymentMode).HasMaxLength(8).IsRequired();
        builder.Property(x => x.CarrierOrderId).HasMaxLength(32);
        builder.Property(x => x.CarrierShipmentId).HasMaxLength(32);
        builder.Property(x => x.Awb).HasMaxLength(40);
        builder.Property(x => x.CourierName).HasMaxLength(100);
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.Property(x => x.QuotedCourierId).HasMaxLength(32);
        builder.Property(x => x.QuoteError).HasMaxLength(500);
        builder.Property(x => x.ReturnReason).HasMaxLength(16);
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

        builder.HasMany(x => x.Charges)
            .WithOne()
            .HasForeignKey(x => x.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Charges).UsePropertyAccessMode(PropertyAccessMode.Field);
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

internal sealed class ShipmentChargeConfiguration : IEntityTypeConfiguration<ShipmentCharge>
{
    public void Configure(EntityTypeBuilder<ShipmentCharge> builder)
    {
        builder.ToTable("ShipmentCharges", ShippingModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One charge per trip of a shipment.
        builder.HasIndex(x => new { x.ShipmentId, x.Trip }).IsUnique();

        builder.Property(x => x.Trip).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.CorrectedBy).HasMaxLength(64);
        builder.Property(x => x.Note).HasMaxLength(200);

        builder.Ignore(x => x.IsIncurred);
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
        builder.Property(x => x.Pincode).HasMaxLength(6);
    }
}

internal sealed class CodReceivableConfiguration : IEntityTypeConfiguration<CodReceivable>
{
    public void Configure(EntityTypeBuilder<CodReceivable> builder)
    {
        builder.ToTable("CodReceivables", ShippingModule.SchemaName, table =>
            table.HasCheckConstraint("CK_CodReceivables_Expected", "[Expected] > 0"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One per delivered parcel, however often the courier reports the delivery.
        builder.HasIndex(x => x.ShipmentId).IsUnique();

        // Remittance rows find their parcel by AWB; staff filter by what is still owed.
        builder.HasIndex(x => x.Awb);
        builder.HasIndex(x => new { x.Status, x.DeliveredAtUtc });
        builder.HasIndex(x => x.OrderPartId);

        builder.HasOne<Shipment>()
            .WithMany()
            .HasForeignKey(x => x.ShipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Awb).HasMaxLength(CodRemittanceLine.AwbMaxLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.WriteOffNote).HasMaxLength(500);
        builder.Property(x => x.WrittenOffBy).HasMaxLength(64);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Ignore(x => x.IsCashIn);
    }
}

internal sealed class CodRemittanceConfiguration : IEntityTypeConfiguration<CodRemittance>
{
    public void Configure(EntityTypeBuilder<CodRemittance> builder)
    {
        builder.ToTable("CodRemittances", ShippingModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // A report is counted once.
        builder.HasIndex(x => x.Reference).IsUnique();

        builder.Property(x => x.Reference).HasMaxLength(CodRemittance.ReferenceMaxLength).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(CodRemittance.FileNameMaxLength).IsRequired();
        builder.Property(x => x.UploadedBy).HasMaxLength(64);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.CodRemittanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.Total);
    }
}

internal sealed class CodRemittanceLineConfiguration : IEntityTypeConfiguration<CodRemittanceLine>
{
    public void Configure(EntityTypeBuilder<CodRemittanceLine> builder)
    {
        builder.ToTable("CodRemittanceLines", ShippingModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // Unmatched rows are looked up by AWB when a delivery is reported.
        builder.HasIndex(x => new { x.Awb, x.ReceivableId });

        builder.Property(x => x.Awb).HasMaxLength(CodRemittanceLine.AwbMaxLength).IsRequired();
    }
}
