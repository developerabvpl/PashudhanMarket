using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UPBazaar.Infrastructure.Logging;
using UPBazaar.Infrastructure.Persistence.Audit;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps IAuditable entities and writes the audit trail. Rule 8 of the architecture is
/// satisfied simply by implementing IAuditable; nothing else has to opt in.
/// </summary>
public sealed class AuditableEntityInterceptor(IClock clock, ICurrentUser currentUser)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var traceId = Activity.Current?.TraceId.ToString();
        var entries = context.ChangeTracker.Entries<IAuditable>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var logs = new List<AuditLog>(entries.Count);

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedBy = currentUser.UserId;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = now;
                    entry.Entity.ModifiedBy = currentUser.UserId;
                    break;
            }

            logs.Add(new AuditLog
            {
                Module = context.Model.FindEntityType(entry.Metadata.ClrType)?.GetSchema()
                    ?? UPBazaarDbContext.SharedSchema,
                EntityType = entry.Metadata.ClrType.Name,
                EntityPublicId = entry.Entity is Entity domainEntity ? domainEntity.PublicId : Guid.Empty,
                Action = entry.State switch
                {
                    EntityState.Added => AuditAction.Created,
                    EntityState.Deleted => AuditAction.Deleted,
                    _ => AuditAction.Updated,
                },
                Changes = Describe(entry),
                UserId = currentUser.UserId,
                UserName = currentUser.UserName,
                TraceId = traceId,
                OccurredAtUtc = now,
            });
        }

        if (logs.Count > 0)
        {
            context.Set<AuditLog>().AddRange(logs);
        }
    }

    private static string? Describe(EntityEntry<IAuditable> entry)
    {
        var changes = new JsonObject();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;

            if (entry.State == EntityState.Modified && !property.IsModified)
            {
                continue;
            }

            if (SensitiveData.IsSensitive(name))
            {
                changes[name] = SensitiveData.Mask;
                continue;
            }

            changes[name] = entry.State switch
            {
                EntityState.Modified => new JsonObject
                {
                    ["from"] = Stringify(property.OriginalValue),
                    ["to"] = Stringify(property.CurrentValue),
                },
                EntityState.Deleted => Stringify(property.OriginalValue),
                _ => Stringify(property.CurrentValue),
            };
        }

        return changes.Count == 0 ? null : changes.ToJsonString(JsonOptions);
    }

    private static JsonNode? Stringify(object? value) => value switch
    {
        null => null,
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        _ => JsonValue.Create(value.ToString()),
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
}
