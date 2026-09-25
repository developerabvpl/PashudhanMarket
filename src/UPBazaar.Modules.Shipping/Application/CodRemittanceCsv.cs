using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>
/// Reads the courier's remittance report: a CSV with a row per parcel paid for.
///
/// Only two columns matter - the AWB and the amount paid - and they are found by what their
/// headings say rather than where they sit, because the exact headings of Shiprocket's report
/// have not been checked against a real one yet, and reports gain columns over time. An AWB
/// column is one whose heading mentions "AWB" or "tracking"; the amount is the heading that
/// mentions "remitted" if there is one, else "COD", else "amount". Everything else is ignored.
/// </summary>
public static partial class CodRemittanceCsv
{
    public const int MaxRows = 5000;

    public static Result<IReadOnlyList<(string Awb, decimal Amount)>> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var records = Records(text.TrimStart('\uFEFF')).Where(r => r.Any(f => f.Length > 0)).ToList();

        if (records.Count < 2)
        {
            return Fail("The file has no rows under its headings.");
        }

        var headings = records[0].Select(Words).ToList();
        var awbColumn = headings.FindIndex(h => h.Any(w => w.StartsWith("awb", StringComparison.Ordinal)) || h.FirstOrDefault() == "tracking");
        var amountColumn = AmountColumn(headings);

        if (awbColumn < 0 || amountColumn < 0)
        {
            return Fail("The file needs an AWB column and an amount column, such as \"AWB\" and \"Remitted Amount\".");
        }

        if (records.Count - 1 > MaxRows)
        {
            return Fail($"The file has more than {MaxRows} rows. Upload it in parts.");
        }

        var rows = new List<(string Awb, decimal Amount)>();

        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];
            var awb = Field(record, awbColumn);

            if (awb.Length == 0)
            {
                continue;
            }

            if (awb.Length > CodRemittanceLine.AwbMaxLength)
            {
                return Fail($"Row {i + 1}: the AWB is too long.");
            }

            if (!TryAmount(Field(record, amountColumn), out var amount))
            {
                return Fail($"Row {i + 1}: \"{Field(record, amountColumn)}\" is not an amount.");
            }

            rows.Add((awb, amount));
        }

        return rows.Count == 0
            ? Fail("No row in the file names an AWB.")
            : Result.Success<IReadOnlyList<(string Awb, decimal Amount)>>(rows);
    }

    /// <summary>The amount paid: not a date, id, UTR or number column that happens to mention it.</summary>
    private static int AmountColumn(List<string[]> headings)
    {
        string[] notAmounts = ["date", "id", "utr", "no", "number", "ref", "reference"];

        int Find(Func<string, bool> word) => headings.FindIndex(h => h.Any(word) && !h.Any(notAmounts.Contains));

        var remitted = Find(w => w.StartsWith("remit", StringComparison.Ordinal));

        if (remitted >= 0)
        {
            return remitted;
        }

        var cod = Find(w => w == "cod");

        return cod >= 0 ? cod : Find(w => w == "amount");
    }

    /// <summary>Rupees as a report writes them: "1,234.50", "₹ 1234.5", "Rs. 1234".</summary>
    private static bool TryAmount(string field, out decimal amount)
    {
        var cleaned = field
            .Replace("₹", string.Empty, StringComparison.Ordinal)
            .Replace("Rs.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Rs", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("INR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount)
            && amount >= 0
            && amount == Math.Round(amount, 2);
    }

    /// <summary>A heading's words, lower case: "COD Remitted Amount (Rs)" is cod, remitted, amount, rs.</summary>
    private static string[] Words(string heading) =>
        [.. NonWord().Split(heading.ToLowerInvariant()).Where(w => w.Length > 0)];

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonWord();

    private static string Field(List<string> record, int column) => column < record.Count ? record[column].Trim() : string.Empty;

    private static Result<IReadOnlyList<(string Awb, decimal Amount)>> Fail(string why) =>
        Result.Failure<IReadOnlyList<(string Awb, decimal Amount)>>(ShippingErrors.CodFileUnreadable(why));

    /// <summary>
    /// Splits CSV into records: commas between fields, quotes around fields that hold commas,
    /// line breaks or quotes, and a doubled quote for a quote inside one.
    /// </summary>
    private static IEnumerable<List<string>> Records(string text)
    {
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    yield return record;
                    record = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }
}
