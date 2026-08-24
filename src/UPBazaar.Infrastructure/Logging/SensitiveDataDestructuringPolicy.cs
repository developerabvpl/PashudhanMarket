using System.Collections.Concurrent;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace UPBazaar.Infrastructure.Logging;

/// <summary>
/// Destructures our own types property by property and masks anything whose name looks
/// sensitive, so logging a whole DTO cannot leak PII or a secret by accident.
/// </summary>
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue? result)
    {
        var type = value.GetType();

        if (!IsOwnType(type))
        {
            result = null;
            return false;
        }

        var properties = Properties.GetOrAdd(
            type,
            static t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToArray());

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
            catch (Exception ex)
            {
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
