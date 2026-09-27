using System.Text.RegularExpressions;

namespace Finyte.Core.Recurring;

// Bank statement lines repeat per-charge details (value dates, masked cards, foreign amounts) that would
// otherwise give every charge from one merchant a different name.
public static class StatementNameCleaner
{
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }
        var cleaned = Regex.Replace(value, @"\bValue\s+Date:.*$", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bCard\s+xx\d+\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b[A-Z]{3}\s+\d[\d,]*\.\d{2}\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length == 0 ? value.Trim() : cleaned;
    }
}
