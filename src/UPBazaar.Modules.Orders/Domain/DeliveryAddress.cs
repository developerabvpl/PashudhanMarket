namespace UPBazaar.Modules.Orders.Domain;

/// <summary>
/// Where an order goes, copied from the checkout form.
///
/// A snapshot, not a reference to a saved address: a buyer who later edits or deletes an address
/// in an address book must not change where an order already on its way is heading.
/// </summary>
public sealed class DeliveryAddress
{
    public const int NameMaxLength = 100;
    public const int LineMaxLength = 200;
    public const int PlaceMaxLength = 100;

    private DeliveryAddress()
    {
    }

    public string FullName { get; private set; } = string.Empty;

    public string Mobile { get; private set; } = string.Empty;

    public string Line1 { get; private set; } = string.Empty;

    public string? Line2 { get; private set; }

    public string? Landmark { get; private set; }

    public string City { get; private set; } = string.Empty;

    public string? District { get; private set; }

    public string State { get; private set; } = string.Empty;

    public string Pincode { get; private set; } = string.Empty;

    /// <summary>Builds an address from input the caller has already validated. Blank optional parts become null.</summary>
    public static DeliveryAddress Create(
        string fullName,
        string mobile,
        string line1,
        string? line2,
        string? landmark,
        string city,
        string? district,
        string state,
        string pincode) => new()
    {
        FullName = fullName.Trim(),
        Mobile = mobile.Trim(),
        Line1 = line1.Trim(),
        Line2 = Blank(line2),
        Landmark = Blank(landmark),
        City = city.Trim(),
        District = Blank(district),
        State = IndianStates.Canonical(state) ?? state.Trim(),
        Pincode = pincode.Trim(),
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// India's states and union territories, spelled the way couriers expect them.
///
/// Checked at checkout because a misspelt state is the commonest reason a courier rejects a
/// shipment, and it is far cheaper to catch at the form than after the order has been handed over.
/// </summary>
public static class IndianStates
{
    public static IReadOnlyList<string> All { get; } =
    [
        "Andaman and Nicobar Islands",
        "Andhra Pradesh",
        "Arunachal Pradesh",
        "Assam",
        "Bihar",
        "Chandigarh",
        "Chhattisgarh",
        "Dadra and Nagar Haveli and Daman and Diu",
        "Delhi",
        "Goa",
        "Gujarat",
        "Haryana",
        "Himachal Pradesh",
        "Jammu and Kashmir",
        "Jharkhand",
        "Karnataka",
        "Kerala",
        "Ladakh",
        "Lakshadweep",
        "Madhya Pradesh",
        "Maharashtra",
        "Manipur",
        "Meghalaya",
        "Mizoram",
        "Nagaland",
        "Odisha",
        "Puducherry",
        "Punjab",
        "Rajasthan",
        "Sikkim",
        "Tamil Nadu",
        "Telangana",
        "Tripura",
        "Uttar Pradesh",
        "Uttarakhand",
        "West Bengal",
    ];

    /// <summary>The listed spelling of a state, ignoring case and surrounding space; null if it is not one.</summary>
    public static string? Canonical(string? state) =>
        state is null
            ? null
            : All.FirstOrDefault(s => string.Equals(s, state.Trim(), StringComparison.OrdinalIgnoreCase));
}
