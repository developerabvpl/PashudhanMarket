using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.SharedKernel.Outbox;

namespace UPBazaar.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", UPBazaarDbContext.SharedSchema);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Module).HasMaxLength(64);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.Error).HasColumnType("nvarchar(max)");
        builder.Property(x => x.OccurredAtUtc).IsRequired();

        // An event may only ever be enqueued once.
        builder.HasIndex(x => x.EventId).IsUnique();

        // Covers the processor's "next unprocessed batch, oldest first" scan.
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc });
    }
}
