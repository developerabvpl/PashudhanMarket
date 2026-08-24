using System.Reflection;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Shipping;

public sealed class ShippingModuleSchema : IModuleSchema
{
    public const string SchemaName = "shipping";

    public string Name => "Shipping";

    public string Schema => SchemaName;

    public Assembly Assembly => typeof(ShippingModuleSchema).Assembly;
}
