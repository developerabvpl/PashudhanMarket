using Azure.Security.KeyVault.Secrets;
using UPBazaar.Api.Configuration;

namespace UPBazaar.IntegrationTests.Configuration;

/// <summary>The shared vault's other secrets stay out; ours lose their prefix.</summary>
public sealed class UpBazaarSecretManagerTests
{
    private readonly UpBazaarSecretManager _manager = new();

    [Theory]
    [InlineData("UPBazaar--ConnectionStrings--UPBazaar", true)]
    [InlineData("upbazaar--Jwt--SigningKey", true)]
    [InlineData("sqlconnectionstring", false)]
    [InlineData("jwtforPashudhanGrievance", false)]
    [InlineData("UPBazaarConnection", false)]
    public void Loads_only_secrets_named_for_this_application(string name, bool loaded)
    {
        _manager.Load(new SecretProperties(name)).ShouldBe(loaded);
    }

    [Theory]
    [InlineData("UPBazaar--ConnectionStrings--UPBazaar", "ConnectionStrings:UPBazaar")]
    [InlineData("UPBazaar--Jwt--SigningKey", "Jwt:SigningKey")]
    public void Drops_the_prefix_and_turns_double_dashes_into_sections(string name, string key)
    {
        _manager.GetKey(new KeyVaultSecret(name, "value")).ShouldBe(key);
    }
}
