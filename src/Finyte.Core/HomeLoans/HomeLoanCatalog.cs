using System.Text.RegularExpressions;
using Finyte.Core.Accounts;
using Finyte.Core.Recurring;

namespace Finyte.Core.HomeLoans;

public sealed record HomeLoanCategory(string Key, string Label, string Group);

public sealed record HomeLoanFlagRule(string Kind, string Label, string Advice, IReadOnlyList<string> Keywords);

public sealed record HomeLoanMerchantName(string Name, string? PaymentType, string GroupKey, IReadOnlyList<string> Words, string OwnName, bool Brand);

public static partial class HomeLoanCatalog
{
    public const string Living = "living";
    public const string Commitment = "commitment";
    public const string Excluded = "excluded";
    public const string Savings = "savings";
    public const string Unlabelled = "unlabelled";
    public const decimal LargeDepositThreshold = 1000m;

    public static IReadOnlyList<HomeLoanCategory> Categories { get; } =
    [
        new("food-groceries", "Food & groceries", Living),
        new("residence", "Rates, utilities & home maintenance", Living),
        new("phone-internet-media", "Phone, internet & media", Living),
        new("transport", "Transport", Living),
        new("clothing-personal-care", "Clothing & personal care", Living),
        new("recreation", "Recreation & holidays", Living),
        new("medical", "Medical & health", Living),
        new("general-insurance", "Insurance (home, contents, car)", Living),
        new("life-insurance", "Life, accident & illness insurance", Living),
        new("childcare-education", "Childcare, public education & dependants", Living),
        new("private-school", "Private school fees", Living),
        new("strata", "Strata / body corporate", Living),
        new("other-living", "Other living expenses", Living),
        new("rent", "Rent", Commitment),
        new("loan-repayments", "Loan repayments", Commitment),
        new("credit-card", "Credit card repayments", Commitment),
        new("bnpl", "Buy now pay later", Commitment),
        new("child-support", "Child support", Commitment),
        new("savings-elsewhere", "Moved to savings (another account)", Savings),
        new("not-expense", "Not an expense", Excluded)
    ];

    public static IReadOnlyList<string> IncomeKinds { get; } = ["salary", "other-income", "refund", "gift", "not-income"];

    public static IReadOnlyList<HomeLoanFlagRule> FlagRules { get; } =
    [
        new("gambling", "Gambling", "Lenders look closely at regular gambling spending.",
            ["sportsbet", "ladbrokes", "bet365", "pointsbet", "neds", "betfair", "unibet", "palmerbet", "playup", "tabcorp", "tab com au", "crown casino", "star casino", "lotterywest", "the lott", "oz lotto"]),
        new("bnpl", "Buy now pay later", "Buy now pay later accounts count as debts, even with a nil balance.",
            ["afterpay", "zip pay", "zippay", "zipmoney", "zip money", "zip co", "klarna", "humm", "latitude pay", "openpay"]),
        new("fees", "Dishonour or overdrawn fees", "Lenders ask about dishonoured payments and overdrawn accounts.",
            ["dishonour", "dishonor", "dishonoured", "overdrawn", "overdraw", "insufficient funds", "returned payment", "late payment fee"]),
        new("payday", "Short-term lenders", "Payday or short-term loans are usually queried.",
            ["nimble", "cash converters", "moneyme", "wallet wizard", "fair go finance", "cash stop", "credit24", "beforepay"])
    ];

    public static HomeLoanCategory? Category(string? key) => Categories.FirstOrDefault(x => x.Key == key);

    public static string DefaultCategory(string tagName) => tagName.Trim().ToLowerInvariant() switch
    {
        "groceries" or "eating out" => "food-groceries",
        "bills and utilities" or "home" => "residence",
        "transport" or "fuel" => "transport",
        "health" => "medical",
        "shopping" => "clothing-personal-care",
        "entertainment" or "travel" => "recreation",
        "subscriptions" => "phone-internet-media",
        "transfers" => "not-expense",
        "rent" => "rent",
        "savings" => "savings-elsewhere",
        _ => "other-living"
    };

    public static string PayerKey(string? merchantName, string? description)
    {
        var text = StatementNameCleaner.Clean(string.IsNullOrWhiteSpace(merchantName) ? description : merchantName).ToLowerInvariant();
        var words = NonWord().Split(text).Where(x => x.Length > 0 && !x.Any(char.IsDigit));
        var key = string.Join(' ', words);
        return key.Length > 200 ? key[..200].TrimEnd() : key;
    }

    public static HomeLoanFlagRule? DebitFlag(string? merchantName, string? description)
    {
        var text = $" {string.Join(' ', NonWord().Split($"{merchantName} {description}".ToLowerInvariant()).Where(x => x.Length > 0))} ";
        return FlagRules.FirstOrDefault(rule => rule.Keywords.Any(keyword => text.Contains($" {keyword} ", StringComparison.Ordinal)));
    }

    public static HomeLoanMerchantName Describe(string? merchantName, string? description)
    {
        var raw = StatementNameCleaner.Clean(string.IsNullOrWhiteSpace(merchantName) ? description : merchantName);
        var all = $" {string.Join(' ', NonWord().Split($"{description} {merchantName}".ToLowerInvariant()).Where(x => x.Length > 0))} ";
        var type = all.Contains(" direct debit ", StringComparison.Ordinal) ? "Direct debit"
            : all.Contains(" transfer to ", StringComparison.Ordinal) || all.Contains(" transfer from ", StringComparison.Ordinal) || all.Contains(" osko ", StringComparison.Ordinal) ? "Transfer"
            : all.Contains(" bpay ", StringComparison.Ordinal) ? "BPAY"
            : all.Contains(" payto ", StringComparison.Ordinal) ? "PayTo"
            : all.Contains(" card xx", StringComparison.Ordinal) || all.Contains(" eftpos ", StringComparison.Ordinal) || all.Contains(" visa ", StringComparison.Ordinal) ? "Card"
            : null;
        var tokens = Token().Matches(raw).Select(x => x.Value.Trim('-', '\'')).Where(x => x.Length > 0).ToList();
        var lower = tokens.Select(x => x.ToLowerInvariant()).ToList();
        foreach (var prefix in NamePrefixes)
        {
            if (lower.Count > prefix.Length && lower.Take(prefix.Length).SequenceEqual(prefix))
            {
                tokens.RemoveRange(0, prefix.Length);
                lower.RemoveRange(0, prefix.Length);
                break;
            }
        }
        var kept = tokens.Where((x, i) => x.Any(char.IsLetter) && (!x.Any(char.IsDigit) || x.Contains('-')) && !NoiseWords.Contains(lower[i])
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
                foreach (var noise in TransferNoise)
                {
                    if (kept.Count >= noise.Length && kept.Take(noise.Length).Select(x => x.ToLowerInvariant()).SequenceEqual(noise))
                    {
                        kept.RemoveRange(0, noise.Length);
                        removed = true;
                        break;
                    }
                }
            }
            kept.RemoveAll(x => x.Equals("app", StringComparison.OrdinalIgnoreCase) && kept.Count == 1);
            var suffix = lower.FirstOrDefault(x => x.StartsWith("xx", StringComparison.Ordinal) && x.Length > 2 && x[2..].All(char.IsDigit));
            var direction = all.Contains(" transfer from ", StringComparison.Ordinal) ? "from" : "to";
            var transfer = kept.Count > 0 ? string.Join(' ', kept) : suffix is not null ? $"Transfer {direction} account ending {suffix[2..]}" : $"Transfer {direction} another account";
            transfer = char.ToUpperInvariant(transfer[0]) + transfer[1..];
            return new HomeLoanMerchantName(transfer, type, $"transfer:{transfer.ToLowerInvariant()}", [transfer.ToLowerInvariant()], transfer, false);
        }
        while (kept.Count > 1 && Processors.Contains(kept[0].ToLowerInvariant()))
        {
            kept.RemoveAt(0);
        }
        var words = kept.Select(x => x.ToLowerInvariant().Replace('-', ' ').Replace("'", "")).ToList();
        var normalized = string.Join(' ', words);
        var own = TitleCase(NameTokens(tokens, lower, kept));
        if (MerchantKeywordCatalog.Suggest(normalized) is { AtStart: true } match && !CategoryWords.Contains(match.Keyword))
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
        while (start < tokens.Count - 1 && Processors.Contains(lower[start]))
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
                if (StateCodes.Contains(lower[i]) && (i == tokens.Count - 1 || lower[i + 1] is "au" or "aus"))
                {
                    cut = i - start >= 3 ? i - 1 : i;
                    boundary = true;
                    break;
                }
            }
        }
        var candidate = tokens.Skip(start).Take(cut - start).Where(x => IsNameWord(x)).ToList();
        var shop = candidate.FindIndex(x => ShopWords.Contains(x.ToLowerInvariant()));
        var name = shop is >= 1 and <= 3 ? candidate.Take(shop + 1).ToList() : candidate.Take(boundary ? 3 : 2).ToList();
        return name.Count > 0 ? name : kept.Take(2).ToList();
    }

    private static bool IsNameWord(string token)
    {
        var lower = token.ToLowerInvariant();
        return token.Any(char.IsLetter) && (!token.Any(char.IsDigit) || token.Contains('-')) && !NoiseWords.Contains(lower)
            && !(lower.StartsWith("xx", StringComparison.Ordinal) && lower.Length > 2);
    }

    private static readonly HashSet<string> StateCodes = ["ql", "qld", "ns", "nsw", "vi", "vic", "wa", "sa", "ta", "tas", "ac", "act", "nt"];

    private static readonly HashSet<string> ShopWords =
    [
        "chemist", "pharmacy", "cafe", "bakery", "butcher", "clinic", "dental", "dentist", "medical", "physio", "physiotherapy", "newsagency", "newsagent",
        "hotel", "tavern", "bar", "pub", "pizza", "pizzeria", "sushi", "restaurant", "kitchen", "florist", "salon", "barber", "vet", "veterinary", "hospital",
        "pathology", "optometrist", "gym", "fitness", "school", "childcare", "laundry", "laundromat", "liquor", "bottleshop", "takeaway", "bistro", "supermarket",
        "pharmacist", "surgery", "dispensary", "nursery", "hardware", "tyres", "mechanic", "motors", "cinema", "cinemas", "club"
    ];
    public static string TitleCase(IEnumerable<string> words) => string.Join(' ', words.Select(word =>
        word.Length is >= 2 and <= 3 && word.All(char.IsUpper) && !CommonShortWords.Contains(word.ToLowerInvariant()) ? word
        : string.Join('-', word.Split('-').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()))));

    private static readonly HashSet<string> CommonShortWords =
    [
        "and", "the", "bar", "pub", "car", "cat", "dog", "red", "big", "new", "old", "top", "one", "two", "fit", "gym", "art", "bay", "sun", "hub", "box",
        "kid", "pet", "tea", "mr", "mrs", "dr", "st", "of", "on", "in", "at", "by", "my", "go", "eat", "bed", "spa", "inn", "zoo", "ice", "hot", "fun", "bar"
    ];

    private static readonly HashSet<string> CategoryWords =
    [
        "cafe", "coffee", "sushi", "pizza", "bakery", "restaurant", "bar", "pub", "supermarket", "butcher", "fruit market", "parking", "taxi", "fuel",
        "pharmacy", "chemist", "medical", "dental", "dentist", "physio", "clinic", "pathology", "hospital", "insurance", "council", "rent", "transfer to"
    ];
    private static readonly string[][] NamePrefixes = new[]
    {
        "fast transfer to", "fast transfer from", "transfer to", "transfer from", "direct debit", "direct credit", "payment to", "payment from",
        "purchase from", "visa purchase", "osko payment", "pending", "payto", "bpay", "eftpos", "purchase"
    }.Select(x => x.Split(' ')).OrderByDescending(x => x.Length).ToArray();

    private static readonly HashSet<string> NoiseWords = ["aus", "au", "australia", "nsw", "vic", "vi", "qld", "sa", "wa", "tas", "nt", "act", "ql", "ta", "ac", "pty", "ltd", "the", "www", "com", "net", "org", "card", "xx", "value", "date", "cash", "out"];

    private static readonly string[][] TransferNoise = new[] { "other bank", "commbank app", "commbank", "netbank", "internet banking", "mobile banking", "to", "from" }
        .Select(x => x.Split(' ')).OrderByDescending(x => x.Length).ToArray();

    private static readonly HashSet<string> Processors = ["sq", "sp", "zlr", "pp", "paypal", "ls", "sumup", "tst", "ezi", "mr"];

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'\-]*")]
    private static partial Regex Token();
}
