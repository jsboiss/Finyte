using System.Text.RegularExpressions;
using Finyte.Core.Accounts;
using Finyte.Core.Recurring;

namespace Finyte.Core.HomeLoans;

public sealed record HomeLoanCategory(string Key, string Label, string Group);

public sealed record HomeLoanFlagRule(string Kind, string Label, string Advice, IReadOnlyList<string> Keywords);

public sealed record HomeLoanPayerName(string Key, string Name, bool Typed, string? SuggestedKind);

public sealed record HomeLoanMerchantName(string Name, string? PaymentType, string GroupKey, IReadOnlyList<string> Words, string OwnName, bool Brand);

public static partial class HomeLoanCatalog
{
    public const string Living = "living";
    public const string Commitment = "commitment";
    public const string Excluded = "excluded";
    public const string Savings = "savings";
    public const string Unlabelled = "unlabelled";
    public static IReadOnlyList<string> Groups { get; } = [Living, Commitment, Savings, Excluded];

    public static IReadOnlyList<string> IncomeKinds { get; } = ["salary", "other-income", "refund", "gift", "not-income"];

    public static HomeLoanLargeDeposits LargeDeposits => HomeLoanRules.Current.LargeDeposits;

    public static IReadOnlyList<HomeLoanCategory> Categories => HomeLoanRules.Current.Categories;

    public static IReadOnlyList<HomeLoanFlagRule> FlagRules => HomeLoanRules.Current.Flags;

    public static HomeLoanCategory? Category(string? key) => Categories.FirstOrDefault(x => x.Key == key);

    public static string DefaultCategory(string tagName) =>
        HomeLoanRules.Current.TagCategories.GetValueOrDefault(tagName.Trim().ToLowerInvariant()) ?? HomeLoanRules.Current.FallbackCategory;

    public static string PayerKey(string? merchantName, string? description)
    {
        var text = StatementNameCleaner.Clean(string.IsNullOrWhiteSpace(merchantName) ? description : merchantName).ToLowerInvariant();
        var words = NonWord().Split(text).Where(x => x.Length > 0 && !x.Any(char.IsDigit));
        var key = string.Join(' ', words);
        return key.Length > 200 ? key[..200].TrimEnd() : key;
    }

    public static HomeLoanPayerName DescribePayer(string? merchantName, string? description)
    {
        var raw = StatementNameCleaner.Clean(string.IsNullOrWhiteSpace(merchantName) ? description : merchantName);
        var all = $" {string.Join(' ', NonWord().Split($"{description} {merchantName}".ToLowerInvariant()).Where(x => x.Length > 0))} ";
        var tokens = Token().Matches(raw).Select(x => x.Value.Trim('-', '\'')).Where(x => x.Length > 0).ToList();
        var removed = true;
        while (removed && tokens.Count > 1)
        {
            removed = false;
            foreach (var prefix in Lists.Value.CreditPrefixes)
            {
                if (tokens.Count > prefix.Length && tokens.Take(prefix.Length).Select(x => x.ToLowerInvariant()).SequenceEqual(prefix))
                {
                    tokens.RemoveRange(0, prefix.Length);
                    removed = true;
                    break;
                }
            }
            while (tokens.Count > 1 && tokens[0].All(char.IsDigit))
            {
                tokens.RemoveAt(0);
                removed = true;
            }
        }
        var name = new List<string>();
        foreach (var token in tokens)
        {
            if (token.Any(char.IsDigit))
            {
                if (name.Count > 0)
                {
                    break;
                }
                continue;
            }
            var lower = token.ToLowerInvariant();
            if (Lists.Value.NoiseWords.Contains(lower) || Lists.Value.CreditNoise.Contains(lower))
            {
                continue;
            }
            name.Add(token);
            if (name.Count == 3)
            {
                break;
            }
        }
        var kind = IncomeKinds.FirstOrDefault(x => HomeLoanRules.Current.IncomeHints.TryGetValue(x, out var hints) && hints.Any(hint => all.Contains($" {hint} ", StringComparison.Ordinal)));
        if (kind is null && all.Split(' ').Any(word => HomeLoanRules.Current.SalaryReferencePrefixes.Any(prefix =>
                word.Length >= prefix.Length + 3 && word.StartsWith(prefix, StringComparison.Ordinal) && word[prefix.Length..].All(char.IsDigit))))
        {
            kind = "salary";
        }
        if (name.Count == 0)
        {
            return new HomeLoanPayerName(PayerKey(merchantName, description), raw, false, kind);
        }
        return new HomeLoanPayerName($"payer:{string.Join(' ', name.Select(x => x.ToLowerInvariant()))}", TitleCase(name), name.Any(IsTyped), kind);
    }

    private static bool IsTyped(string word) => word.Any(char.IsLower) && word.Skip(1).Any(char.IsUpper);

    public static string TitleCase(IEnumerable<string> words) => string.Join(' ', words.Select(word =>
        (word.Length is >= 2 and <= 3 && word.All(char.IsUpper) && !Lists.Value.ShortWords.Contains(word.ToLowerInvariant())) || IsTyped(word) ? word
        : string.Join('-', word.Split('-').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()))));

    public static HomeLoanFlagRule? DebitFlag(string? merchantName, string? description)
    {
        var text = $" {string.Join(' ', NonWord().Split($"{merchantName} {description}".ToLowerInvariant()).Where(x => x.Length > 0))} ";
        return FlagRules.FirstOrDefault(rule => rule.Keywords.Any(keyword => text.Contains($" {keyword} ", StringComparison.Ordinal)));
    }

    public static HomeLoanMerchantName Describe(string? merchantName, string? description)
    {
        var raw = StatementNameCleaner.Clean(string.IsNullOrWhiteSpace(merchantName) ? description : merchantName);
        var all = $" {string.Join(' ', NonWord().Split($"{description} {merchantName}".ToLowerInvariant()).Where(x => x.Length > 0))} ";
        var type = HomeLoanRules.Current.PaymentTypes.FirstOrDefault(x => x.Phrases.Any(phrase => all.Contains($" {phrase}", StringComparison.Ordinal)))?.Type;
        var tokens = Token().Matches(raw).Select(x => x.Value.Trim('-', '\'')).Where(x => x.Length > 0).ToList();
        var lower = tokens.Select(x => x.ToLowerInvariant()).ToList();
        foreach (var prefix in Lists.Value.NamePrefixes)
        {
            if (lower.Count > prefix.Length && lower.Take(prefix.Length).SequenceEqual(prefix))
            {
                tokens.RemoveRange(0, prefix.Length);
                lower.RemoveRange(0, prefix.Length);
                break;
            }
        }
        var kept = tokens.Where((x, i) => x.Any(char.IsLetter) && (!x.Any(char.IsDigit) || x.Contains('-')) && !Lists.Value.NoiseWords.Contains(lower[i])
            && !(lower[i].StartsWith("xx", StringComparison.Ordinal) && lower[i].Length > 2)).ToList();
        if (kept.Count == 0)
        {
            return new HomeLoanMerchantName(raw, type, raw.ToLowerInvariant(), [raw.ToLowerInvariant()], raw, false);
        }
        if (type == "Transfer")
        {
            var removed = true;
            while (removed && kept.Count > 0)
            {
                removed = false;
                foreach (var noise in Lists.Value.TransferNoise)
                {
                    if (kept.Count >= noise.Length && kept.Take(noise.Length).Select(x => x.ToLowerInvariant()).SequenceEqual(noise))
                    {
                        kept.RemoveRange(0, noise.Length);
                        removed = true;
                        break;
                    }
                }
            }
            var suffix = lower.FirstOrDefault(x => x.StartsWith("xx", StringComparison.Ordinal) && x.Length > 2 && x[2..].All(char.IsDigit));
            var direction = all.Contains(" transfer from ", StringComparison.Ordinal) ? "from" : "to";
            var transfer = kept.Count > 0 ? string.Join(' ', kept) : suffix is not null ? $"Transfer {direction} account ending {suffix[2..]}" : $"Transfer {direction} another account";
            transfer = char.ToUpperInvariant(transfer[0]) + transfer[1..];
            return new HomeLoanMerchantName(transfer, type, $"transfer:{transfer.ToLowerInvariant()}", [transfer.ToLowerInvariant()], transfer, false);
        }
        while (kept.Count > 1 && Lists.Value.Processors.Contains(kept[0].ToLowerInvariant()))
        {
            kept.RemoveAt(0);
        }
        var words = kept.Select(x => x.ToLowerInvariant().Replace('-', ' ').Replace("'", "")).ToList();
        var normalized = string.Join(' ', words);
        var own = TitleCase(NameTokens(tokens, lower, kept));
        if (MerchantKeywordCatalog.Suggest(normalized) is { AtStart: true } match && !Lists.Value.CategoryWords.Contains(match.Keyword))
        {
            var length = 0;
            var used = 0;
            var target = match.Keyword.Split(' ').Length;
            while (used < kept.Count && length < target)
            {
                length += words[used].Split(' ').Length;
                used++;
            }
            return new HomeLoanMerchantName(TitleCase(kept.Take(used)), type, $"brand:{match.Keyword}", words, own, true);
        }
        return new HomeLoanMerchantName(own, type, $"word:{words[0]}", words, own, false);
    }

    private static List<string> NameTokens(List<string> tokens, List<string> lower, List<string> kept)
    {
        var start = 0;
        while (start < tokens.Count - 1 && Lists.Value.Processors.Contains(lower[start]))
        {
            start++;
        }
        var cut = tokens.Count;
        var boundary = false;
        for (var i = start + 1; i < tokens.Count; i++)
        {
            if (lower[i].All(char.IsDigit))
            {
                cut = i;
                boundary = true;
                break;
            }
        }
        if (!boundary)
        {
            for (var i = start + 1; i < tokens.Count; i++)
            {
                if (Lists.Value.StateCodes.Contains(lower[i]) && (i == tokens.Count - 1 || lower[i + 1] is "au" or "aus"))
                {
                    cut = i - start >= 3 ? i - 1 : i;
                    boundary = true;
                    break;
                }
            }
        }
        var candidate = tokens.Skip(start).Take(cut - start).Where(x => IsNameWord(x)).ToList();
        var shop = candidate.FindIndex(x => Lists.Value.ShopWords.Contains(x.ToLowerInvariant()));
        var name = shop is >= 1 and <= 3 ? candidate.Take(shop + 1).ToList() : candidate.Take(boundary ? 3 : 2).ToList();
        return name.Count > 0 ? name : kept.Take(2).ToList();
    }

    private static bool IsNameWord(string token)
    {
        var lower = token.ToLowerInvariant();
        return token.Any(char.IsLetter) && (!token.Any(char.IsDigit) || token.Contains('-')) && !Lists.Value.NoiseWords.Contains(lower)
            && !(lower.StartsWith("xx", StringComparison.Ordinal) && lower.Length > 2);
    }

    private static readonly Lazy<WordLists> Lists = new(() => new WordLists(HomeLoanRules.Current.StatementNames));

    private sealed class WordLists(HomeLoanStatementWords words)
    {
        public string[][] NamePrefixes { get; } = Phrases(words.NamePrefixes);
        public string[][] CreditPrefixes { get; } = Phrases(words.CreditPrefixes);
        public string[][] TransferNoise { get; } = Phrases(words.TransferNoise);
        public HashSet<string> NoiseWords { get; } = [.. words.NoiseWords];
        public HashSet<string> CreditNoise { get; } = [.. words.CreditNoise];
        public HashSet<string> Processors { get; } = [.. words.Processors];
        public HashSet<string> StateCodes { get; } = [.. words.StateCodes];
        public HashSet<string> ShopWords { get; } = [.. words.ShopWords];
        public HashSet<string> CategoryWords { get; } = [.. words.CategoryWords];
        public HashSet<string> ShortWords { get; } = [.. words.ShortWords];

        private static string[][] Phrases(IEnumerable<string> phrases) => phrases.Select(x => x.Split(' ')).OrderByDescending(x => x.Length).ToArray();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'\-]*")]
    private static partial Regex Token();
}
