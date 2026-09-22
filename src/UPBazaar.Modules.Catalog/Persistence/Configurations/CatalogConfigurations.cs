using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.Modules.Catalog.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", CatalogModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.Slug).IsUnique();

        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(Slug.MaxLength).IsRequired();

        // Restrict: removing a parent must be a decision about its children, not a cascade.
        builder.HasOne(x => x.Parent)
            .WithMany()
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", CatalogModule.SchemaName, table =>
        {
            table.HasCheckConstraint("CK_Products_Price", "[Price] >= 0");

            // All four package values, or none; and a package that exists is a real one.
            table.HasCheckConstraint(
                "CK_Products_Package",
                "([WeightGrams] IS NULL AND [LengthCm] IS NULL AND [BreadthCm] IS NULL AND [HeightCm] IS NULL) "
                + "OR ([WeightGrams] > 0 AND [LengthCm] > 0 AND [BreadthCm] > 0 AND [HeightCm] > 0)");
        });

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.Sku).IsUnique();

        // The storefront's two hot queries: a category's active products, and everything a
        // seller has listed.
        builder.HasIndex(x => new { x.Status, x.CategoryId });
        builder.HasIndex(x => x.SellerId);

        builder.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(Slug.MaxLength).IsRequired();
        builder.Property(x => x.Brand).HasMaxLength(128);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ReviewNote).HasMaxLength(1000);

        // Centimetres to one decimal place, which is as fine as any courier measures.
        builder.Property(x => x.LengthCm).HasPrecision(6, 1);
        builder.Property(x => x.BreadthCm).HasPrecision(6, 1);
        builder.Property(x => x.HeightCm).HasPrecision(6, 1);

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.DomainEvents);
    }
}
