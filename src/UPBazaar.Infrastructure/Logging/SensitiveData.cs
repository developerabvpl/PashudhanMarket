namespace UPBazaar.Infrastructure.Logging;

/// <summary>
/// Single source of truth for what must never reach a log sink or the audit trail.
/// Both the Serilog destructuring policy and the audit interceptor consult this.
/// </summary>
public static class SensitiveData
{
    public const string Mask = "***redacted***";

    private static readonly string[] Fragments =
    [
        "password", "passwd", "secret", "token", "apikey", "api_key", "authorization",
        "signature", "otp", "pin", "cvv", "cardnumber", "card_number", "pan", "upi",
        "aadhaar", "aadhar", "accountnumber", "account_number", "ifsc", "email",
        "phone", "mobile", "contactnumber", "gstin", "connectionstring",
    ];

    /// <summary>True when a property with this name must be masked.</summary>
    public static bool IsSensitive(string propertyName)
    {
        foreach (var fragment in Fragments)
        {
            if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
