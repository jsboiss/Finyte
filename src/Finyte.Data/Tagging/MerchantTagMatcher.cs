using System.Text.RegularExpressions;

namespace Finyte.Data.Tagging;

public static partial class MerchantTagMatcher
{
    // Preserve words (including place names) rather than guessing which parts of a merchant are noise.
    public static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : string.Join(' ', NonAlphaNumericRegex().Replace(value.ToLowerInvariant(), " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool Matches(string? merchantName, string? description, string merchantKey)
    {
        var transactionKey = Normalize(string.IsNullOrWhiteSpace(merchantName) ? description : merchantName);
        return merchantKey.Length > 0
            && (transactionKey == merchantKey || transactionKey.StartsWith($"{merchantKey} ", StringComparison.Ordinal));
    }

    [GeneratedRegex("[^\\p{L}\\p{N}]+")]
    private static partial Regex NonAlphaNumericRegex();
}
