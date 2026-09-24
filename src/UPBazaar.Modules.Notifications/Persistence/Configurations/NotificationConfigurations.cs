using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Notifications.Domain;

namespace UPBazaar.Modules.Notifications.Persistence.Configurations;

internal sealed class NotificationMessageConfiguration : IEntityTypeConfiguration<NotificationMessage>
{
    public void Configure(EntityTypeBuilder<NotificationMessage> builder)
    {
        builder.ToTable("Messages", NotificationsModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // One message per kind, event and recipient, however often the event is delivered.
        builder.HasIndex(x => x.Key).IsUnique();

        // Support looking up what a buyer or seller was sent.
        builder.HasIndex(x => new { x.Recipient, x.CreatedAtUtc });

        builder.Property(x => x.Key).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Template).HasMaxLength(48).IsRequired();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(x => x.Recipient).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(256);
        builder.Property(x => x.Body).HasMaxLength(NotificationMessage.BodyMaxLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(500);
    }
}
