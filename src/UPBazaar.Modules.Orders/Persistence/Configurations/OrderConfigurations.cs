using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Orders.Domain;

namespace UPBazaar.Modules.Orders.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", OrdersModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.Number).IsUnique();

        // "My orders", newest first.
        builder.HasIndex(x => new { x.BuyerId, x.PlacedAtUtc });

        // The expiry job's scan for unpaid orders past their deadline.
        builder.HasIndex(x => new { x.Status, x.PaymentDueAtUtc });

        builder.Property(x => x.Number).HasMaxLength(OrderNumber.MaxLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PaymentReference).HasMaxLength(100);
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Ignore(x => x.Subtotal);
        builder.Ignore(x => x.Total);
        builder.Ignore(x => x.CanCancel);

        builder.ComplexProperty(x => x.DeliveryAddress, address =>
        {
            address.Property(a => a.FullName).HasColumnName("DeliveryFullName")
                .HasMaxLength(DeliveryAddress.NameMaxLength).IsRequired();
            address.Property(a => a.Mobile).HasColumnName("DeliveryMobile").HasMaxLength(10).IsRequired();
            address.Property(a => a.Line1).HasColumnName("DeliveryLine1")
                .HasMaxLength(DeliveryAddress.LineMaxLength).IsRequired();
            address.Property(a => a.Line2).HasColumnName("DeliveryLine2").HasMaxLength(DeliveryAddress.LineMaxLength);
            address.Property(a => a.Landmark).HasColumnName("DeliveryLandmark")
                .HasMaxLength(DeliveryAddress.LineMaxLength);
            address.Property(a => a.City).HasColumnName("DeliveryCity")
                .HasMaxLength(DeliveryAddress.PlaceMaxLength).IsRequired();
            address.Property(a => a.District).HasColumnName("DeliveryDistrict")
                .HasMaxLength(DeliveryAddress.PlaceMaxLength);
            address.Property(a => a.State).HasColumnName("DeliveryState")
                .HasMaxLength(DeliveryAddress.PlaceMaxLength).IsRequired();
            address.Property(a => a.Pincode).HasColumnName("DeliveryPincode").HasMaxLength(6).IsRequired();
        });

        builder.HasMany(x => x.Parts)
            .WithOne()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Parts).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderPartConfiguration : IEntityTypeConfiguration<OrderPart>
{
    public void Configure(EntityTypeBuilder<OrderPart> builder)
    {
        builder.ToTable("OrderParts", OrdersModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // A seller's order queue, once the seller portal reads it.
        builder.HasIndex(x => new { x.SellerId, x.Status });

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.ReturnCondition).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ReturnNote).HasMaxLength(500);
        builder.Property(x => x.ReturnInspectedBy).HasMaxLength(64);

        builder.Ignore(x => x.Subtotal);
        builder.Ignore(x => x.IsComing);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.OrderPartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLines", OrdersModule.SchemaName, table =>
            table.HasCheckConstraint("CK_OrderLines_Quantity", "[Quantity] > 0"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.ProductId);

        builder.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();

        builder.Ignore(x => x.LineTotal);
    }
}
