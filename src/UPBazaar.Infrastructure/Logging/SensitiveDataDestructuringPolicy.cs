using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace UPBazaar.Infrastructure.Logging;

/// <summary>
/// Destructures this solution's own types property by property, masking anything whose name
/// looks sensitive.
///
/// Without it, logging a whole DTO is one careless line away from putting a phone number or a
/// gateway signature into a log file. Third-party types are left to Serilog's defaults, since
/// masking by name would be guesswork there.
/// </summary>
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        var type = value.GetType();

        if (!IsOwnType(type))
        {
            result = null;
            return false;
        }

        var properties = Properties.GetOrAdd(
            type,
            static t => [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)]);

        var logged = new List<LogEventProperty>(properties.Length);

        foreach (var property in properties)
        {
            if (SensitiveData.IsSensitive(property.Name))
            {
                logged.Add(new LogEventProperty(property.Name, new ScalarValue(SensitiveData.Mask)));
                continue;
            }

            object? propertyValue;

            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (TargetInvocationException ex)
            {
                // A computed property that throws must not take the log line down with it.
                logged.Add(new LogEventProperty(property.Name, new ScalarValue($"<{ex.GetType().Name}>")));
                continue;
            }

            logged.Add(new LogEventProperty(
                property.Name,
                propertyValueFactory.CreatePropertyValue(propertyValue, destructureObjects: true)));
        }

        result = new StructureValue(logged, type.Name);

        return true;
    }

    private static bool IsOwnType(Type type) =>
        type.Assembly.GetName().Name?.StartsWith("UPBazaar", StringComparison.Ordinal) == true;
}
