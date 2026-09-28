using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPBazaar.Modules.Identity.Domain;

namespace UPBazaar.Modules.Identity.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", IdentityModule.SchemaName);

        // Failed sign-ins counted in parallel must not overwrite each other and slip under the
        // lockout: the later save fails instead.
        builder.Property(x => x.AccessFailedCount).IsConcurrencyToken();

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // Filtered unique indexes: a buyer may have no email and a staff member no mobile, and
        // SQL Server would otherwise treat every NULL as a collision.
        builder.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
        builder.HasIndex(x => x.Mobile).IsUnique().HasFilter("[Mobile] IS NOT NULL");
        builder.HasIndex(x => new { x.UserType, x.Status });

        builder.Property(x => x.UserType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Mobile).HasMaxLength(10);
        builder.Property(x => x.PasswordHash).HasMaxLength(512);
        builder.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PreferredLanguage).HasMaxLength(8).IsRequired().HasDefaultValue("en");
        builder.Property(x => x.TwoFactorSecret).HasMaxLength(128);

        builder.HasMany(x => x.Roles)
            .WithOne()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", IdentityModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.Name).IsUnique();

        builder.Property(x => x.Name).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(256);

        builder.HasMany(x => x.Permissions)
            .WithOne()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions", IdentityModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.Module);

        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(256);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", IdentityModule.SchemaName);

        builder.HasKey(x => new { x.RoleId, x.PermissionId });

        builder.HasOne(x => x.Permission)
            .WithMany()
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles", IdentityModule.SchemaName);

        builder.HasKey(x => new { x.UserId, x.RoleId });

        builder.Property(x => x.AssignedBy).HasMaxLength(64);
        builder.Property(x => x.AssignedAtUtc).IsRequired();

        builder.HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", IdentityModule.SchemaName);

        // A token presented twice at once must rotate once: the later save fails, and it is refused.
        builder.Property(x => x.RevokedAtUtc).IsConcurrencyToken();

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // Every refresh lands here first, so the lookup must be a unique index seek.
        builder.HasIndex(x => x.TokenHash).IsUnique();

        // Reuse detection revokes a whole family at once.
        builder.HasIndex(x => x.FamilyId);
        builder.HasIndex(x => new { x.UserId, x.RevokedAtUtc });

        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
        builder.Property(x => x.CreatedByIp).HasMaxLength(64);
        builder.Property(x => x.RevokedByIp).HasMaxLength(64);
        builder.Property(x => x.DeviceInfo).HasMaxLength(256);
        builder.Property(x => x.RevocationReason).HasConversion<string>().HasMaxLength(24);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("OtpChallenges", IdentityModule.SchemaName);

        // Two guesses at once must not both count as the first: the later save fails instead.
        builder.Property(x => x.Attempts).IsConcurrencyToken();

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        // Verification looks up the newest usable challenge for a target and purpose; rate
        // limiting counts recent rows for the same target.
        builder.HasIndex(x => new { x.Target, x.Purpose, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.RequestedByIp, x.CreatedAtUtc });

        builder.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Target).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RequestedByIp).HasMaxLength(64);
    }
}

internal sealed class LoginAuditConfiguration : IEntityTypeConfiguration<LoginAudit>
{
    public void Configure(EntityTypeBuilder<LoginAudit> builder)
    {
        builder.ToTable("LoginAudit", IdentityModule.SchemaName);

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.IpAddress, x.OccurredAtUtc });
        builder.HasIndex(x => x.OccurredAtUtc);

        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.FailureReason).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.AttemptedIdentifier).HasMaxLength(256);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);

        // No foreign key: attempts against accounts that do not exist must still be recorded.
    }
}
