using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Infrastructure.Persistence.Audit;

namespace UPBazaar.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLog", "audit");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Module).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Changes).HasColumnType("nvarchar(max)");
        builder.Property(x => x.UserId).HasMaxLength(64);
        builder.Property(x => x.UserName).HasMaxLength(256);
        builder.Property(x => x.TraceId).HasMaxLength(64);
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(x => new { x.EntityType, x.EntityPublicId });
        builder.HasIndex(x => x.OccurredAtUtc);
    }
}
