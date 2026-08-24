using System.Reflection;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Payments;

public sealed class PaymentsModuleSchema : IModuleSchema
{
    public const string SchemaName = "payments";

    public string Name => "Payments";

    public string Schema => SchemaName;

    public Assembly Assembly => typeof(PaymentsModuleSchema).Assembly;
}
