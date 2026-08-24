using System.Reflection;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Ordering;

public sealed class OrderingModuleSchema : IModuleSchema
{
    public const string SchemaName = "ordering";

    public string Name => "Ordering";

    public string Schema => SchemaName;

    public Assembly Assembly => typeof(OrderingModuleSchema).Assembly;
}
