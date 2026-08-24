using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UPBazaar.Infrastructure.Logging;
using UPBazaar.Infrastructure.Persistence.Shared;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps <see cref="IAuditable"/> entities and writes the audit trail.
///
/// Runs inside the caller's transaction, so an audit row can never survive a change that
/// rolled back. Implementing the interface is the whole opt-in.
/// </summary>
public sealed class AuditInterceptor(IClock clock, ICurrentUser currentUser, ICorrelationContext correlation)
    : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

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

        var entries = context.ChangeTracker.Entries<IAuditable>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

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

                default:
                    break;
            }

            logs.Add(new AuditLog
            {
                Module = context.Model.FindEntityType(entry.Metadata.ClrType)?.GetSchema()
                    ?? UPBazaarDbContext.SharedSchema,
                EntityType = entry.Metadata.ClrType.Name,
                EntityPublicId = entry.Entity is Entity entity ? entity.PublicId : Guid.Empty,
                Action = entry.State switch
                {
                    EntityState.Added => AuditAction.Created,
                    EntityState.Deleted => AuditAction.Deleted,
                    _ => AuditAction.Updated,
                },
                Changes = Describe(entry),
                UserId = currentUser.UserId,
                UserName = currentUser.UserName,
                CorrelationId = correlation.CorrelationId,
                OccurredAtUtc = now,
            });
        }

        context.Set<AuditLog>().AddRange(logs);
    }

    /// <summary>
    /// Serialises what actually changed. On an update only modified properties are recorded,
    /// with both values, so the log answers "what did this person change" rather than
    /// restating the whole row.
    /// </summary>
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

    private static JsonValue? Stringify(object? value) => value switch
    {
        null => null,
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        _ => JsonValue.Create(value.ToString()),
    };
}
