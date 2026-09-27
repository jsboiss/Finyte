using System.Text.RegularExpressions;
using Finyte.Core.Accounts;

namespace Finyte.Core.Transfers;

public static partial class InternalTransferDetector
{
    public const string Detected = "detected";
    public const string Manual = "manual";
    public const string Excluded = "excluded";
    public const int MaxNicknames = 10;
    public const int MaxNicknameLength = 60;

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "transfer", "transfers", "tfr", "trf", "to", "from", "fast", "osko", "payid", "payment", "pay", "internet", "netbank", "banking", "bank", "app", "online", "direct", "credit", "debit", "ref", "reference", "the", "a", "of", "and", "internal", "value", "date"
    };

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
            .Select(x => (x.Id, Number: Digits(x.AccountNumber), Nicknames: NormaliseNicknames(x.TransferNicknames)))
            .Where(x => x.Number.Length >= 4 || x.Nicknames.Count > 0)
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
                if (run.Length < 6 && !MaskPrefix().IsMatch(text[..match.Index]))
                {
                    continue;
                }

                foreach (var candidate in candidates.Where(x => x.Number.Length >= 4 && x.Number.EndsWith(run, StringComparison.Ordinal)))
                {
                    matches.Add(candidate.Id);
                }
            }

            foreach (var candidate in candidates.Where(x => x.Nicknames.Any(y => ContainsPhrase(text, y))))
            {
                matches.Add(candidate.Id);
            }
        }

        return matches.Count == 1 ? matches.Single() : null;
    }

    public static List<string> NormaliseNicknames(IEnumerable<string>? nicknames)
    {
        return nicknames is null ? [] : nicknames
            .Select(x => Whitespace().Replace(x.Trim(), " "))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? RulePhrase(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var words = Whitespace().Split(description.Trim())
            .Where(x => !x.Any(char.IsAsciiDigit))
            .Select(x => x.Trim(':', ';', ',', '.', '-', '*', '#', '/', '\\', '(', ')'))
            .Where(x => x.Length > 0)
            .ToList();
        var phrase = string.Join(' ', words);
        while (phrase.Length > MaxNicknameLength)
        {
            words.RemoveAt(words.Count - 1);
            phrase = string.Join(' ', words);
        }

        return words.Count >= 2 && words.Any(x => !GenericWords.Contains(x)) ? phrase : null;
    }

    public static List<string>? MergeRule(IReadOnlyList<string> nicknames, string phrase)
    {
        if (nicknames.Any(x => ContainsPhrase(phrase, x)))
        {
            return null;
        }
        var words = phrase.Split(' ');
        for (var index = 0; index < nicknames.Count; index++)
        {
            var existing = nicknames[index].Split(' ');
            var shared = words.Zip(existing).TakeWhile(x => string.Equals(x.First, x.Second, StringComparison.OrdinalIgnoreCase)).Select(x => x.Second).ToList();
            if (shared.Count >= 2 && shared.Any(x => !GenericWords.Contains(x)))
            {
                var merged = nicknames.ToList();
                merged[index] = string.Join(' ', shared);
                return merged;
            }
        }
        return nicknames.Count >= MaxNicknames ? null : [.. nicknames, phrase];
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        return Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(phrase).Replace(@"\ ", @"\s+")}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);
    }

    private static string Digits(string? value)
    {
        return value is null ? "" : new string(value.Where(char.IsAsciiDigit).ToArray());
    }

    [GeneratedRegex(@"(?<![\d])(\d{4,})(?![\d])")]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"\bcard\s*[xX*#]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CardPrefix();

    [GeneratedRegex(@"[xX*#•…\.]\s?$")]
    private static partial Regex MaskPrefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
