using System.Reflection;

namespace UPBazaar.SharedKernel.Modules;

/// <summary>
/// Registered once per module so the shared DbContext knows which assemblies to scan for
/// IEntityTypeConfiguration classes, and which SQL schema the module owns.
/// </summary>
public interface IModuleSchema
{
    /// <summary>Module name, for example Catalog.</summary>
    string Name { get; }

    /// <summary>SQL schema the module owns, for example catalog.</summary>
    string Schema { get; }

    /// <summary>Assembly holding the module entity configurations, handlers and validators.</summary>
    Assembly Assembly { get; }
}
