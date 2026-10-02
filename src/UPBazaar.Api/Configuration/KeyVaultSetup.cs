using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;

namespace UPBazaar.Api.Configuration;

/// <summary>
/// Reads secrets - the database connection string first among them - from Azure Key Vault, so
/// they live in neither appsettings, the repository nor a developer's machine.
///
/// Switched on by one setting, <c>KeyVault:Uri</c> (for example
/// <c>https://upbazaar-dev.vault.azure.net/</c>). The URI is not a secret; put it in user-secrets,
/// appsettings.{Environment}.json or <c>UPBAZAAR_KeyVault__Uri</c>. Left empty, nothing is read
/// and configuration is exactly as before, which is what the tests rely on.
///
/// Secret names use <c>--</c> for the section separator, Key Vault not allowing <c>:</c>: the
/// connection string is the secret <c>ConnectionStrings--UPBazaar</c>, the signing key
/// <c>Jwt--SigningKey</c>.
///
/// Sign-in is <see cref="DefaultAzureCredential"/>: the Azure CLI or Visual Studio account on a
/// developer's machine (<c>az login</c>), the managed identity once deployed to Azure. Either needs
/// permission to read secrets in the vault (the "Key Vault Secrets User" role).
/// </summary>
public static class KeyVaultSetup
{
    public const string UriKey = "KeyVault:Uri";

    /// <summary>
    /// Adds the vault named by <see cref="UriKey"/> in the configuration built so far, if there is
    /// one. Call it before any source that should be able to override the vault - such as the
    /// <c>UPBAZAAR_</c> environment variables a test run or a one-off command sets.
    /// </summary>
    public static IConfigurationBuilder AddUpBazaarKeyVault(this IConfigurationBuilder builder, IConfiguration current)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(current);

        var uri = current[UriKey];

        if (string.IsNullOrWhiteSpace(uri))
        {
            return builder;
        }

        return builder.AddAzureKeyVault(
            new Uri(uri),
            new DefaultAzureCredential(),
            new AzureKeyVaultConfigurationOptions
            {
                // Rotated secrets are picked up without a restart.
                ReloadInterval = TimeSpan.FromMinutes(30),
            });
    }
}
