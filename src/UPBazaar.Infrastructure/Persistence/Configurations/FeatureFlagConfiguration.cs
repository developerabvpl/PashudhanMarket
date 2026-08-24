using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Infrastructure.Persistence.Shared;

namespace UPBazaar.Infrastructure.Persistence.Configurations;

internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("FeatureFlags", UPBazaarDbContext.SharedSchema);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512);
        builder.Property(x => x.UpdatedBy).HasMaxLength(64);
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Property(x => x.RolloutPercentage).HasDefaultValue(0);

        builder.HasIndex(x => x.Key).IsUnique();

        // A percentage outside 0-100 is meaningless; reject it at the database, not just in code.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_FeatureFlags_RolloutPercentage",
            "[RolloutPercentage] >= 0 AND [RolloutPercentage] <= 100"));
    }
}
