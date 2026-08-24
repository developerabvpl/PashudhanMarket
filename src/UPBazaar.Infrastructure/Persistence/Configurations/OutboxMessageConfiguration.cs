using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Infrastructure.Persistence.Outbox;

namespace UPBazaar.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessage", "shared");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Error).HasColumnType("nvarchar(max)");
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(x => x.ProcessedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(x => x.EventId).IsUnique();

        // Covers the processor's "next unprocessed batch" scan.
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc });
    }
}
