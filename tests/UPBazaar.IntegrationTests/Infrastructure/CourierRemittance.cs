using System.Net.Http.Headers;
using System.Text;

namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// The courier paying over the cash it collected for a parcel, as staff record it: an upload of
/// its remittance report. Sellers are paid for a cash-on-delivery parcel only after this.
/// </summary>
internal static class CourierRemittance
{
    public static async Task PayAsync(HttpClient admin, string awb, decimal amount)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent($"UTR{Guid.NewGuid():N}"[..16].ToUpperInvariant()), "reference");
        form.Add(new StringContent(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), "remittedOn");

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"AWB,Remitted Amount\n{awb},{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "remittance.csv");

        (await admin.PostAsync(new Uri("/api/v1/admin/shipping/cod/remittances", UriKind.Relative), form)).EnsureSuccessStatusCode();
    }
}
