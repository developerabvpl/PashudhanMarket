using UPBazaar.Infrastructure.Logging;

namespace UPBazaar.UnitTests.Infrastructure;

public sealed class SensitiveDataTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("passwordHash")]
    [InlineData("ApiKey")]
    [InlineData("WebhookSecret")]
    [InlineData("CustomerEmail")]
    [InlineData("MobileNumber")]
    [InlineData("Gstin")]
    [InlineData("IfscCode")]
    [InlineData("X-Razorpay-Signature")]
    public void Anything_that_looks_like_a_secret_or_pii_is_flagged(string propertyName)
    {
        SensitiveData.IsSensitive(propertyName).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Sku")]
    [InlineData("ProductName")]
    [InlineData("Quantity")]
    [InlineData("OrderNumber")]
    [InlineData("CreatedAtUtc")]
    public void Ordinary_business_fields_are_left_alone(string propertyName)
    {
        SensitiveData.IsSensitive(propertyName).ShouldBeFalse();
    }

    [Fact]
    public void Matching_ignores_case_so_naming_style_cannot_defeat_it()
    {
        SensitiveData.IsSensitive("PASSWORD").ShouldBeTrue();
        SensitiveData.IsSensitive("password").ShouldBeTrue();
    }
}
