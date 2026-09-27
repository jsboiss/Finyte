using System.Text.RegularExpressions;
using Finyte.Core.Accounts;

namespace Finyte.Core.Transfers;

public static partial class InternalTransferDetector
{
    public const string Detected = "detected";
    public const string Manual = "manual";
    public const string Excluded = "excluded";

    public static bool Apply(Transaction transaction, IReadOnlyList<Account> accounts)
    {
        if (transaction.InternalTransferSource is Manual or Excluded)
        {
            return false;
        }

        var counterpartyId = Detect(transaction, accounts);
        var source = counterpartyId is null ? null : Detected;
        if (transaction.InternalTransferAccountId == counterpartyId && transaction.InternalTransferSource == source)
        {
            return false;
        }

        transaction.InternalTransferAccountId = counterpartyId;
        transaction.InternalTransferSource = source;
        return true;
    }

    public static Guid? Detect(Transaction transaction, IReadOnlyList<Account> accounts)
    {
        var candidates = accounts
            .Where(x => x.TenantId == transaction.TenantId && x.Id != transaction.AccountId)
            .Select(x => (x.Id, Number: Digits(x.AccountNumber)))
            .Where(x => x.Number.Length >= 4)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var matches = new HashSet<Guid>();
        foreach (var text in new[] { transaction.Description, transaction.Reference })
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in DigitRun().Matches(text))
            {
                if (CardPrefix().IsMatch(text[..match.Index]))
                {
                    continue;
                }

                var run = match.Groups[1].Value;
                foreach (var candidate in candidates.Where(x => x.Number.EndsWith(run, StringComparison.Ordinal)))
                {
                    matches.Add(candidate.Id);
                }
            }
        }

        return matches.Count == 1 ? matches.Single() : null;
    }

    private static string Digits(string? value)
    {
        return value is null ? "" : new string(value.Where(char.IsAsciiDigit).ToArray());
    }

    [GeneratedRegex(@"(?<![\d])(\d{4,})(?![\d])")]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"\bcard\s*[xX*#]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CardPrefix();
}
