using System.Text.Json;

namespace Finyte.Core.HomeLoans;

public sealed record HomeLoanLargeDeposits(decimal Threshold, string Label, string Advice);

public sealed record HomeLoanPaymentType(string Type, IReadOnlyList<string> Phrases);

public sealed record HomeLoanStatementWords(
    IReadOnlyList<string> NamePrefixes,
    IReadOnlyList<string> CreditPrefixes,
    IReadOnlyList<string> TransferNoise,
    IReadOnlyList<string> NoiseWords,
    IReadOnlyList<string> CreditNoise,
    IReadOnlyList<string> Processors,
    IReadOnlyList<string> StateCodes,
    IReadOnlyList<string> ShopWords,
    IReadOnlyList<string> CategoryWords,
    IReadOnlyList<string> ShortWords);

public sealed record HomeLoanRules(
    HomeLoanLargeDeposits LargeDeposits,
    IReadOnlyList<HomeLoanCategory> Categories,
    string FallbackCategory,
    IReadOnlyDictionary<string, string> TagCategories,
    IReadOnlyList<HomeLoanFlagRule> Flags,
    IReadOnlyDictionary<string, IReadOnlyList<string>> IncomeHints,
    IReadOnlyList<string> SalaryReferencePrefixes,
    IReadOnlyDictionary<string, string> TransferTagHints,
    IReadOnlyList<HomeLoanPaymentType> PaymentTypes,
    HomeLoanStatementWords StatementNames)
{
    private static readonly Lazy<HomeLoanRules> Data = new(Load);

    public static HomeLoanRules Current => Data.Value;

    private static HomeLoanRules Load()
    {
        using var stream = typeof(HomeLoanRules).Assembly.GetManifestResourceStream("Finyte.Core.HomeLoans.home-loan-rules.json")
            ?? throw new InvalidOperationException("The home loan rules are missing from the build.");
        var rules = JsonSerializer.Deserialize<HomeLoanRules>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The home loan rules could not be read.");
        var keys = rules.Categories.Select(x => x.Key).ToList();
        var problem = keys.GroupBy(x => x).Where(x => x.Count() > 1).Select(x => $"The lender category '{x.Key}' is listed more than once.")
            .Concat(rules.Categories.Where(x => !HomeLoanCatalog.Groups.Contains(x.Group)).Select(x => $"The lender category '{x.Key}' has an unknown group '{x.Group}'."))
            .Concat(rules.TagCategories.Where(x => !keys.Contains(x.Value)).Select(x => $"The tag '{x.Key}' points to an unknown lender category '{x.Value}'."))
            .Concat(keys.Contains(rules.FallbackCategory) ? [] : [$"The fallback lender category '{rules.FallbackCategory}' is not listed."])
            .Concat(rules.IncomeHints.Keys.Where(x => !HomeLoanCatalog.IncomeKinds.Contains(x)).Select(x => $"The income hint '{x}' is not an income type."))
            .FirstOrDefault();
        return problem is null ? rules : throw new InvalidOperationException(problem);
    }
}
