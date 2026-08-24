using System.Reflection;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Catalog;

/// <summary>Declares the schema this module owns and the assembly the host should scan.</summary>
public sealed class CatalogModuleSchema : IModuleSchema
{
    public const string SchemaName = "catalog";

    public string Name => "Catalog";

    public string Schema => SchemaName;

    public Assembly Assembly => typeof(CatalogModuleSchema).Assembly;
}
