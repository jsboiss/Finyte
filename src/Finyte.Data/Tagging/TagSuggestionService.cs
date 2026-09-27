using Finyte.Core.Accounts;
using Finyte.Core.Recurring;
using Finyte.Data.Analytics;
using Finyte.Data.Tenancy;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Tagging;

public sealed class TagSuggestionService(FinyteDbContext dbContext, TenantCalendars calendars, TransactionTagService tagService,
    IProjectionInvalidator projectionInvalidator)
{
    private const string DefaultColor = "#64748b";

    public async Task<TagSuggestionsResponse> Get(Guid tenantId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var calendar = await calendars.For(tenantId, cancellationToken);
        var end = to ?? calendar.Today;
        var start = from ?? end.AddDays(-365);
        if (end < start || end.DayNumber - start.DayNumber > 1827)
        {
            throw new ArgumentException("Choose a date range in order, spanning at most five years.");
        }
        var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var accountIds = accounts.Where(AccountPreferences.IncludeInAnalytics).Select(x => x.Id).ToArray();
        var rows = await dbContext.Transactions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Amount < 0 && accountIds.Contains(x.AccountId)
                && x.PostedAt >= calendar.StartOf(start) && x.PostedAt < calendar.EndExclusive(end))
            .ExcludeInternalTransfers(dbContext, tenantId)
            .Select(x => new SpendRow(x.Amount, x.Currency, x.MerchantName, x.Description, x.PrimaryCategory, x.SecondaryCategory,
                x.TagAssignments.Any(y => y.Tag != null && y.Tag.TenantId == tenantId)))
            .ToListAsync(cancellationToken);
        var tags = await dbContext.TransactionTags.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

        var coverage = rows.GroupBy(x => x.Currency.ToUpperInvariant()).OrderBy(x => x.Key)
            .Select(x => new TagCoverage(x.Key, x.Where(y => y.Tagged).Sum(y => MinorUnits(y.Amount)), x.Sum(y => MinorUnits(y.Amount))))
            .ToList();

        var merchants = new Dictionary<string, MerchantBuilder>();
        foreach (var group in rows.Where(x => !x.Tagged).GroupBy(x => MerchantTagMatcher.Normalize(StatementNameCleaner.Clean(x.MerchantName ?? x.Description))))
        {
            if (group.Key.Length == 0)
            {
                continue;
            }
            var display = StatementNameCleaner.Clean(group.First().MerchantName ?? group.First().Description);
            var keyword = MerchantKeywordCatalog.Suggest(group.Key);
            // A keyword at the start lets one rule cover every branch of the merchant, e.g. each store of a chain.
            var ruleName = keyword is { AtStart: true, Generic: false }
                ? string.Join(' ', display.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(keyword.Keyword.Split(' ').Length))
                : display;
            var ruleKey = MerchantTagMatcher.Normalize(ruleName);
            if (ruleKey.Length == 0 || ruleName.Length > 256 || !group.All(x => MerchantTagMatcher.Matches(x.MerchantName, x.Description, ruleKey)))
            {
                continue;
            }
            if (!merchants.TryGetValue(ruleKey, out var builder))
            {
                builder = new MerchantBuilder(ruleName);
                merchants[ruleKey] = builder;
            }
            builder.Add(display, group.ToList(), keyword, tags);
        }

        var suggested = merchants.Values.Where(x => x.TagName is not null).Select(x => x.Build())
            .GroupBy(x => x.Suggestion.TagName!, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var existing = tags.FirstOrDefault(y => string.Equals(y.Name, x.Key, StringComparison.OrdinalIgnoreCase));
                return new TagSuggestionGroup(existing?.Name ?? x.Key, existing?.Id, existing?.Color ?? MerchantKeywordCatalog.Starter(x.Key)?.Color ?? DefaultColor,
                    x.Select(y => y.Merchant).OrderByDescending(y => y.SpendMinorUnits).ThenBy(y => y.RuleMerchantName, StringComparer.OrdinalIgnoreCase).ToList());
            })
            .OrderByDescending(x => x.Merchants.Sum(y => y.SpendMinorUnits)).ThenBy(x => x.TagName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var needsTag = merchants.Values.Where(x => x.TagName is null).Select(x => x.Build().Merchant)
            .OrderByDescending(x => x.SpendMinorUnits).ThenBy(x => x.RuleMerchantName, StringComparer.OrdinalIgnoreCase).Take(50).ToList();
        return new TagSuggestionsResponse(start, end, coverage, suggested, needsTag);
    }

    public async Task<AcceptTagSuggestionsResponse> Accept(Guid tenantId, AcceptTagSuggestionsRequest request, CancellationToken cancellationToken)
    {
        var items = request.Items ?? [];
        if (items.Count is 0 or > 200 || items.Any(x => x is null))
        {
            throw new ArgumentException("Accept between 1 and 200 suggestions at a time.");
        }
        foreach (var item in items)
        {
            var name = item.MerchantName?.Trim();
            if (string.IsNullOrEmpty(MerchantTagMatcher.Normalize(name)) || name!.Length > 256)
            {
                throw new ArgumentException("Each merchant name must contain letters or numbers and be at most 256 characters.");
            }
            if ((item.TagId is null) == string.IsNullOrWhiteSpace(item.TagName) || item.TagName?.Trim().Length > 80)
            {
                throw new ArgumentException("Give each suggestion either an existing tag or a tag name of up to 80 characters.");
            }
        }

        await using var databaseTransaction = await tagService.BeginMutation(tenantId, cancellationToken);
        var tags = await dbContext.TransactionTags.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var rules = await tagService.GetRules(tenantId, cancellationToken);
        var createdTags = 0;
        var createdRules = 0;
        foreach (var item in items)
        {
            TransactionTag tag;
            if (item.TagId is { } tagId)
            {
                tag = tags.SingleOrDefault(x => x.Id == tagId) ?? throw new KeyNotFoundException("Tag not found.");
            }
            else
            {
                var tagName = item.TagName!.Trim();
                var existing = tags.FirstOrDefault(x => string.Equals(x.Name, tagName, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    var starter = MerchantKeywordCatalog.Starter(tagName);
                    existing = new TransactionTag { TenantId = tenantId, Name = starter?.Name ?? tagName, Color = starter?.Color ?? DefaultColor, CreatedAt = DateTimeOffset.UtcNow };
                    dbContext.TransactionTags.Add(existing);
                    tags.Add(existing);
                    createdTags++;
                }
                tag = existing;
            }
            var merchantName = item.MerchantName!.Trim();
            var merchantKey = MerchantTagMatcher.Normalize(merchantName);
            if (rules.Any(x => x.MerchantKey == merchantKey && x.TagId == tag.Id)
                || dbContext.MerchantTagRules.Local.Any(x => x.TenantId == tenantId && x.MerchantKey == merchantKey && x.TagId == tag.Id))
            {
                continue;
            }
            dbContext.MerchantTagRules.Add(new MerchantTagRule
            {
                TenantId = tenantId, MerchantName = merchantName, MerchantKey = merchantKey, TagId = tag.Id, Tag = tag, CreatedAt = DateTimeOffset.UtcNow
            });
            createdRules++;
        }
        if (createdRules > 0 || createdTags > 0)
        {
            await tagService.ReconcileTenant(tenantId, cancellationToken);
            await projectionInvalidator.TenantProjectionDataChanged(tenantId, "tag suggestions accepted", cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        return new AcceptTagSuggestionsResponse(createdTags, createdRules);
    }

    private static long MinorUnits(decimal amount) => (long)decimal.Round(Math.Abs(amount) * 100, MidpointRounding.AwayFromZero);

    private sealed record SpendRow(decimal Amount, string Currency, string? MerchantName, string? Description,
        string? PrimaryCategory, string? SecondaryCategory, bool Tagged);

    private sealed class MerchantBuilder(string ruleName)
    {
        private readonly List<string> examples = [];
        private readonly List<SpendRow> rows = [];
        private string? reason;
        public string? TagName { get; private set; }

        public void Add(string display, List<SpendRow> group, MerchantKeywordCatalog.KeywordMatch? keyword, IReadOnlyList<TransactionTag> tags)
        {
            if (!examples.Contains(display, StringComparer.OrdinalIgnoreCase))
            {
                examples.Add(display);
            }
            rows.AddRange(group);
            if (TagName is not null)
            {
                return;
            }
            if (keyword is not null)
            {
                TagName = keyword.TagName;
                reason = $"Keyword: {keyword.Keyword}";
                return;
            }
            // A bank category is only a hint when it names a tag the family has or a starter tag.
            var category = group.SelectMany(x => new[] { x.SecondaryCategory, x.PrimaryCategory }).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .FirstOrDefault(x => tags.Any(y => string.Equals(y.Name, x, StringComparison.OrdinalIgnoreCase)) || MerchantKeywordCatalog.Starter(x) is not null);
            if (category is not null)
            {
                TagName = tags.FirstOrDefault(x => string.Equals(x.Name, category, StringComparison.OrdinalIgnoreCase))?.Name ?? MerchantKeywordCatalog.Starter(category)!.Name;
                reason = $"Bank category: {category}";
            }
        }

        public (MerchantSuggestion Merchant, (string? TagName, string? Reason) Suggestion) Build()
        {
            var currency = rows.GroupBy(x => x.Currency.ToUpperInvariant()).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key;
            return (new MerchantSuggestion(ruleName, examples.Take(3).ToList(), rows.Count, rows.Sum(x => MinorUnits(x.Amount)), currency, reason),
                (TagName, reason));
        }
    }
}

public sealed record TagCoverage(string Currency, long TaggedMinorUnits, long TotalMinorUnits);
public sealed record MerchantSuggestion(string RuleMerchantName, IReadOnlyList<string> Examples, int TransactionCount, long SpendMinorUnits, string Currency, string? Reason);
public sealed record TagSuggestionGroup(string TagName, Guid? TagId, string Color, IReadOnlyList<MerchantSuggestion> Merchants);
public sealed record TagSuggestionsResponse(DateOnly From, DateOnly To, IReadOnlyList<TagCoverage> Coverage, IReadOnlyList<TagSuggestionGroup> Groups, IReadOnlyList<MerchantSuggestion> NeedsTag);
public sealed record AcceptTagSuggestionItem(string? MerchantName, Guid? TagId, string? TagName);
public sealed record AcceptTagSuggestionsRequest(IReadOnlyList<AcceptTagSuggestionItem>? Items);
public sealed record AcceptTagSuggestionsResponse(int CreatedTags, int CreatedRules);
