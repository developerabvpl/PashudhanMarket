using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Infrastructure.Persistence.Idempotency;

namespace UPBazaar.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecord", "shared");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Endpoint).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResponsePayload).HasColumnType("nvarchar(max)");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.CompletedAtUtc).HasColumnType("datetime2");

        // The whole idempotency guarantee rests on this index.
        builder.HasIndex(x => x.Key).IsUnique();
    }
}
