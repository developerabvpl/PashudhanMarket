using System.Globalization;
using System.Net;

namespace UPBazaar.Modules.Notifications.Messages;

/// <summary>
/// Formatting every message shares.
///
/// Texts say "Rs." rather than "₹": the rupee sign is not in the GSM alphabet, and one non-GSM
/// character makes the carrier send the whole SMS as Unicode, at under half the characters per
/// message. Emails, which have no such limit, use the sign.
/// </summary>
internal static class Wording
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>India keeps one time zone and no daylight saving, so a fixed offset is exact.</summary>
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    /// <summary>An amount for a text, such as "Rs. 1,23,450".</summary>
    public static string SmsMoney(decimal amount) => "Rs. " + Group(amount);

    /// <summary>An amount for an email, such as "₹1,23,450.50".</summary>
    public static string EmailMoney(decimal amount) => "₹" + Group(amount);

    /// <summary>A date as Indians write it, in Indian time, such as "30 Sep 2026".</summary>
    public static string Date(DateTime utc) => (utc + Ist).ToString("d MMM yyyy", India);

    /// <summary>Text from a person - a note, a shop name - made safe to put in an email.</summary>
    public static string Html(string text) => WebUtility.HtmlEncode(text);

    /// <summary>
    /// Text cut to fit, with an ellipsis, so a long product name cannot push a text message into
    /// a second, separately charged SMS.
    /// </summary>
    public static string Short(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    /// <summary>A short HTML email: a greeting, paragraphs, and a link to act on.</summary>
    public static string Email(string greeting, IEnumerable<string> paragraphs, string linkText, string linkUrl) =>
        $"<p>{Html(greeting)}</p>"
        + string.Concat(paragraphs.Select(p => $"<p>{p}</p>"))
        + $"<p><a href=\"{Html(linkUrl)}\">{Html(linkText)}</a></p>"
        + "<p>UP Bazaar</p>";

    /// <summary>Rupees and paise with Indian digit grouping; whole rupees without ".00".</summary>
    private static string Group(decimal amount) =>
        amount == decimal.Truncate(amount) ? amount.ToString("#,##0", India) : amount.ToString("#,##0.00", India);
}
