using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Ordering.Domain;

namespace UPBazaar.Modules.Ordering.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Order", OrderingModuleSchema.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.OrderNumber).IsUnique();
        builder.HasIndex(x => new { x.CustomerId, x.PlacedAtUtc });

        builder.Property(x => x.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.DeliveryPostcode).HasMaxLength(12).IsRequired();
        builder.Property(x => x.GatewayOrderId).HasMaxLength(128);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.Property(x => x.Subtotal).HasPrecision(18, 2);
        builder.Property(x => x.ShippingFee).HasPrecision(18, 2);
        builder.Property(x => x.Total).HasPrecision(18, 2);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines).AutoInclude().UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLine", OrderingModuleSchema.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.ProductId);

        builder.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();

        builder.Ignore(x => x.LineTotal);
        builder.Ignore(x => x.DomainEvents);
    }
}
