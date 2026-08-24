using System.Reflection;

namespace UPBazaar.SharedKernel.Modules;

/// <summary>
/// Declares one module to the host. Registered as a singleton so the shared DbContext knows
/// which assemblies to scan for entity configurations, and the dispatcher knows where to find
/// handlers and validators.
/// </summary>
public interface IModule
{
    /// <summary>Module name, for example <c>Catalog</c>.</summary>
    string Name { get; }

    /// <summary>
    /// The SQL schema this module owns, for example <c>catalog</c>. No other module may read
    /// or write inside it.
    /// </summary>
    string Schema { get; }

    /// <summary>Assembly holding the module's entity configurations, handlers and validators.</summary>
    Assembly Assembly { get; }
}
