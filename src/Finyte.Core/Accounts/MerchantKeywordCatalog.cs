namespace Finyte.Core.Accounts;

public static class MerchantKeywordCatalog
{
    public sealed record StarterTag(string Name, string Color);
    public sealed record KeywordMatch(string Keyword, string TagName, bool AtStart, bool Generic);

    public static IReadOnlyList<StarterTag> StarterTags { get; } =
    [
        new("Groceries", "#bbf7d0"), new("Eating out", "#fed7aa"), new("Transport", "#bae6fd"), new("Fuel", "#fde68a"),
        new("Subscriptions", "#ddd6fe"), new("Bills and utilities", "#fecdd3"), new("Health", "#ccfbf1"), new("Shopping", "#e9d5ff"),
        new("Home", "#d9f99d"), new("Entertainment", "#fbcfe8"), new("Travel", "#a5f3fc"), new("Transfers", "#e2e8f0")
    ];

    private static readonly Dictionary<string, string[]> KeywordsByTag = new()
    {
        ["Groceries"] = ["woolworths", "coles", "aldi", "iga", "costco", "harris farm", "foodworks", "drakes", "supermarket", "butcher", "fruit market"],
        ["Eating out"] = ["mcdonalds", "hungry jacks", "kfc", "subway", "dominos", "guzman y gomez", "grill d", "starbucks", "gloria jeans", "zambrero", "nandos", "doordash", "menulog", "uber eats", "cafe", "coffee", "sushi", "pizza", "bakery", "restaurant", "bar", "pub"],
        ["Transport"] = ["translink", "opal", "myki", "uber", "didi", "13cabs", "linkt", "transurban", "parking", "secure parking", "wilson parking", "taxi"],
        ["Fuel"] = ["ampol", "bp", "shell", "caltex", "7 eleven", "eg group", "united petroleum", "puma energy", "metro petroleum", "fuel"],
        ["Subscriptions"] = ["netflix", "spotify", "stan", "disney plus", "binge", "kayo", "youtube", "google one", "amazon prime", "amznprimeau", "audible", "paramount", "crunchyroll", "patreon", "openai", "chatgpt", "adobe", "dropbox", "github", "icloud"],
        ["Bills and utilities"] = ["telstra", "optus", "vodafone", "tpg", "iinet", "aussie broadband", "superloop", "belong", "amaysim", "boost mobile", "agl", "origin energy", "energyaustralia", "alinta", "red energy", "powershop", "simply energy", "urban utilities", "sydney water", "council", "insurance", "nrma", "racq", "aami", "budget direct", "allianz"],
        ["Health"] = ["chemist warehouse", "priceline", "terry white", "pharmacy", "chemist", "medical", "dental", "dentist", "physio", "clinic", "pathology", "hospital", "specsavers", "bupa", "medibank", "hcf", "nib", "ahm"],
        ["Shopping"] = ["amazon", "kmart", "target", "big w", "jb hi fi", "officeworks", "myer", "david jones", "ebay", "the reject shop", "harvey norman", "uniqlo", "rebel", "temu", "shein"],
        ["Home"] = ["bunnings", "ikea", "mitre 10", "spotlight", "fantastic furniture", "adairs", "rent"],
        ["Entertainment"] = ["event cinemas", "hoyts", "reading cinemas", "ticketek", "ticketmaster", "steam", "playstation", "xbox", "nintendo", "timezone"],
        ["Travel"] = ["qantas", "virgin australia", "jetstar", "airbnb", "booking com", "expedia", "webjet", "agoda", "hertz", "avis", "europcar"],
        ["Transfers"] = ["transfer to"]
    };

    private static readonly (string Keyword, string Tag)[] Ordered = KeywordsByTag
        .SelectMany(x => x.Value.Select(y => (Keyword: y, Tag: x.Key)))
        .OrderBy(x => x.Tag == "Transfers").ThenByDescending(x => x.Keyword.Length).ThenBy(x => x.Keyword, StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyCollection<string> KeywordTags => KeywordsByTag.Keys;

    public static KeywordMatch? Suggest(string normalizedMerchant)
    {
        if (string.IsNullOrWhiteSpace(normalizedMerchant))
        {
            return null;
        }
        var padded = $" {normalizedMerchant} ";
        KeywordMatch? best = null;
        foreach (var (keyword, tag) in Ordered)
        {
            if (!padded.Contains($" {keyword} ", StringComparison.Ordinal))
            {
                continue;
            }
            var match = new KeywordMatch(keyword, tag, padded.StartsWith($" {keyword} ", StringComparison.Ordinal), IsGeneric(keyword, tag));
            if (best is null || (match.TagName != "Transfers" && (best.TagName == "Transfers" || (match.AtStart && !best.AtStart))))
            {
                best = match;
            }
        }
        return best;
    }

    private static bool IsGeneric(string keyword, string tag) =>
        tag == "Transfers" || Ordered.Any(x => x.Keyword.StartsWith($"{keyword} ", StringComparison.Ordinal));

    public static StarterTag? Starter(string name) => StarterTags.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
}
