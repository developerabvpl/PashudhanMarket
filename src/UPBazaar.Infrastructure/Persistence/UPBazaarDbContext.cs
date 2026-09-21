using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence.Shared;
using UPBazaar.SharedKernel.Modules;
using UPBazaar.SharedKernel.Outbox;

namespace UPBazaar.Infrastructure.Persistence;

/// <summary>
/// One DbContext for the whole monolith, assembled from the modules registered in the host.
///
/// A single context means a single migration history and a single transaction across a
/// request, which is what lets the outbox be atomic with the state change that raised the
/// event. Isolation between modules is by schema and by the rule that a module only ever
/// touches its own tables, not by separate contexts.
/// </summary>
public sealed class UPBazaarDbContext : DbContext
{
    /// <summary>Schema for platform tables that belong to no single module.</summary>
    public const string SharedSchema = "shared";

    private readonly IReadOnlyCollection<IModule> _modules;

    public UPBazaarDbContext(DbContextOptions<UPBazaarDbContext> options, IEnumerable<IModule> modules)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(modules);

        _modules = [.. modules];
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    /// <summary>Modules composed into this model. Exposed for diagnostics and tests.</summary>
    public IReadOnlyCollection<IModule> Modules => _modules;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SharedSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UPBazaarDbContext).Assembly);

        foreach (var module in _modules)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(module.Assembly);
        }

        ApplyModuleSchemas(modelBuilder);
    }

    /// <summary>
    /// Enforces the storage conventions globally, so no individual configuration has to
    /// remember them: money is decimal(18,2), every timestamp is datetime2, and a string
    /// without an explicit length does not become nvarchar(max).
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2").HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveColumnType("datetime2").HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }

    /// <summary>
    /// Every timestamp is stored in UTC, but datetime2 does not record that, so EF reads each one
    /// back as <see cref="DateTimeKind.Unspecified"/>. Serialised like that it leaves the API
    /// without a trailing Z, and a browser then takes a UTC time for local time - an order placed
    /// at 22:30 IST showed as 17:00. Marking the kind on the way out fixes it once, for every module.
    /// </summary>
    private sealed class UtcDateTimeConverter()
        : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>
    /// Puts every entity declared by a module into that module's schema unless its
    /// configuration said otherwise. Schema-per-module then holds even when someone adds an
    /// entity and forgets the ToTable call.
    /// </summary>
    private void ApplyModuleSchemas(ModelBuilder modelBuilder)
    {
        foreach (var module in _modules)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (entityType.ClrType.Assembly != module.Assembly)
                {
                    continue;
                }

                if (entityType.GetSchema() is null)
                {
                    entityType.SetSchema(module.Schema);
                }
            }
        }
    }
}
