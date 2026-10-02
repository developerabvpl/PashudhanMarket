using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace UPBazaar.Api.Configuration;

/// <summary>
/// Reads only this application's secrets from a vault other applications share.
///
/// The vault holds other systems' connection strings, signing keys and encryption keys. Loading
/// them all would put every one of them in this application's configuration - readable by any
/// code here and one careless log line from leaking - so only secrets named
/// <c>UPBazaar--...</c> are loaded, and the prefix is dropped: <c>UPBazaar--ConnectionStrings--UPBazaar</c>
/// becomes <c>ConnectionStrings:UPBazaar</c>.
/// </summary>
public sealed class UpBazaarSecretManager : KeyVaultSecretManager
{
    public const string Prefix = "UPBazaar--";

    public override bool Load(SecretProperties secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return secret.Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }

    public override string GetKey(KeyVaultSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return secret.Name[Prefix.Length..].Replace("--", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
    }
}
