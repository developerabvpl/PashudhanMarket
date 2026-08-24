using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence.Audit;
using UPBazaar.Infrastructure.Persistence.Idempotency;
using UPBazaar.Infrastructure.Persistence.Outbox;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Infrastructure.Persistence;

/// <summary>
/// One DbContext for the whole monolith, but each module owns its schema and supplies its own
/// IEntityTypeConfiguration classes. Modules never reference each other's entities; the model
/// is assembled here purely so that a single migration history covers the database.
/// </summary>
public sealed class UPBazaarDbContext : DbContext
{
    public const string SharedSchema = "shared";

    private readonly IReadOnlyCollection<IModuleSchema> _modules;

    public UPBazaarDbContext(
        DbContextOptions<UPBazaarDbContext> options,
        IEnumerable<IModuleSchema> modules)
        : base(options) => _modules = modules.ToList();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>Modules registered in this host, exposed for diagnostics and tests.</summary>
    public IReadOnlyCollection<IModuleSchema> Modules => _modules;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SharedSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UPBazaarDbContext).Assembly);

        foreach (var assembly in _modules.Select(m => m.Assembly).Distinct())
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }

    /// <summary>
    /// Enforces the storage conventions globally so no individual configuration has to
    /// remember them: money is decimal(18,2) and every timestamp is datetime2.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2");
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }
}
