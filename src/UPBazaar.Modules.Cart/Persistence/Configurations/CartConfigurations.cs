using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Cart.Domain;

namespace UPBazaar.Modules.Cart.Persistence.Configurations;

internal sealed class ShoppingCartConfiguration : IEntityTypeConfiguration<ShoppingCart>
{
    public void Configure(EntityTypeBuilder<ShoppingCart> builder)
    {
        builder.ToTable("Carts", CartModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One cart per buyer, enforced where two simultaneous first adds cannot both win.
        builder.HasIndex(x => x.BuyerId).IsUnique();

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        builder.ToTable("CartLines", CartModule.SchemaName, table =>
            table.HasCheckConstraint(
                "CK_CartLines_Quantity",
                $"[Quantity] BETWEEN 1 AND {ShoppingCart.MaxQuantityPerLine}"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => new { x.CartId, x.ProductId }).IsUnique();
    }
}
