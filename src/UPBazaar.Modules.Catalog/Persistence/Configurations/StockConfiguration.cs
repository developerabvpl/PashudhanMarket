using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.Modules.Catalog.Persistence.Configurations;

internal sealed class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("Stock", CatalogModuleSchema.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.ProductId).IsUnique();

        builder.Property(x => x.OnHand).IsRequired();
        builder.Property(x => x.Reserved).IsRequired();

        // Required concurrency token: checkouts race for the last unit.
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Ignore(x => x.Available);
        builder.Ignore(x => x.DomainEvents);
    }
}
