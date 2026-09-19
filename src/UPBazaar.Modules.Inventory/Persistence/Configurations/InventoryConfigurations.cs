using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Inventory.Domain;

namespace UPBazaar.Modules.Inventory.Persistence.Configurations;

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        // The database refuses what the domain already refuses, so a bug or a hand-run UPDATE
        // cannot leave stock negative or oversold.
        builder.ToTable("StockItems", InventoryModule.SchemaName, table =>
            table.HasCheckConstraint(
                "CK_StockItems_Quantities",
                "[ReservedQuantity] >= 0 AND [OnHandQuantity] >= [ReservedQuantity]"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.ProductId).IsUnique();

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasMany(x => x.Movements)
            .WithOne()
            .HasForeignKey(x => x.StockItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.AvailableQuantity);
        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", InventoryModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // A product's history, newest first, is the only way the ledger is read.
        builder.HasIndex(x => new { x.StockItemId, x.OccurredAtUtc });

        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(512);
        builder.Property(x => x.Reference).HasMaxLength(128);
        builder.Property(x => x.RecordedBy).HasMaxLength(64);
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", InventoryModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // The expiry job's query: active holds past their time.
        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc });

        builder.Property(x => x.Reference).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ReservationLineConfiguration : IEntityTypeConfiguration<ReservationLine>
{
    public void Configure(EntityTypeBuilder<ReservationLine> builder)
    {
        builder.ToTable("ReservationLines", InventoryModule.SchemaName, table =>
            table.HasCheckConstraint("CK_ReservationLines_Quantity", "[Quantity] > 0"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => new { x.ReservationId, x.ProductId }).IsUnique();
    }
}
