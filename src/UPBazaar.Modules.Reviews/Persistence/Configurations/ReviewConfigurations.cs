using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Reviews.Domain;

namespace UPBazaar.Modules.Reviews.Persistence.Configurations;

internal sealed class ReviewableLineConfiguration : IEntityTypeConfiguration<ReviewableLine>
{
    public void Configure(EntityTypeBuilder<ReviewableLine> builder)
    {
        builder.ToTable("ReviewableLines", ReviewsModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One row per product per parcel, however many times the delivery is reported.
        builder.HasIndex(x => new { x.OrderPartId, x.ProductId }).IsUnique();

        // "May this buyer review this product?"
        builder.HasIndex(x => new { x.BuyerId, x.ProductId });

        builder.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews", ReviewsModule.SchemaName, table =>
            table.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One review per buyer and product.
        builder.HasIndex(x => new { x.BuyerId, x.ProductId }).IsUnique();

        // A product's page, a seller's list, and the moderation queue.
        builder.HasIndex(x => new { x.ProductId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.SellerId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.ContentStatus, x.ContentSubmittedAtUtc });

        builder.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ReviewerName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(Review.TitleMaxLength);
        builder.Property(x => x.Body).HasMaxLength(Review.BodyMaxLength);
        builder.Property(x => x.ContentStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ModerationNote).HasMaxLength(Review.NoteMaxLength);
        builder.Property(x => x.ModeratedBy).HasMaxLength(64);
        builder.Property(x => x.ReplyText).HasMaxLength(Review.ReplyMaxLength);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasMany(x => x.Photos)
            .WithOne()
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Photos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ReviewPhotoConfiguration : IEntityTypeConfiguration<ReviewPhoto>
{
    public void Configure(EntityTypeBuilder<ReviewPhoto> builder)
    {
        builder.ToTable("ReviewPhotos", ReviewsModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.Property(x => x.StorageKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(32).IsRequired();
    }
}
