using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.Extensions.Configuration;
using UPBazaar.Api.Configuration;

namespace UPBazaar.IntegrationTests.Configuration;

/// <summary>
/// The vault is switched on by its address alone. Nothing here reaches Azure: a source is only
/// read when the configuration is built.
/// </summary>
public sealed class KeyVaultSetupTests
{
    [Fact]
    public void Without_an_address_no_vault_is_added()
    {
        var builder = new ConfigurationBuilder();

        builder.AddUpBazaarKeyVault(Settings(null));

        builder.Sources.ShouldBeEmpty();
    }

    [Fact]
    public void A_blank_address_adds_no_vault_either()
    {
        var builder = new ConfigurationBuilder();

        builder.AddUpBazaarKeyVault(Settings("  "));

        builder.Sources.ShouldBeEmpty();
    }

    [Fact]
    public void An_address_adds_the_vault()
    {
        var builder = new ConfigurationBuilder();

        builder.AddUpBazaarKeyVault(Settings("https://upbazaar-test.vault.azure.net/"));

        builder.Sources.ShouldHaveSingleItem().ShouldBeOfType<AzureKeyVaultConfigurationSource>();
    }

    private static IConfiguration Settings(string? vaultUri) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [KeyVaultSetup.UriKey] = vaultUri })
            .Build();
}
