using System.Globalization;
using Finyte.Core.Accounts;
using Finyte.Core.HomeLoans;
using Finyte.Core.Recurring;
using Finyte.Core.Scheduling;
using Finyte.Data.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.HomeLoans;

public sealed class HomeLoanQueries(FinyteDbContext dbContext, TenantCalendars calendars)
{
    public const string DefaultFirstApplicant = "Applicant 1";
    public const string DefaultSecondApplicant = "Applicant 2";
    public static IReadOnlyList<int> PeriodLengths { get; } = [3, 6, 12];
    public static IReadOnlyList<string> Roles { get; } = ["spending", "savings", "excluded"];

    public static bool TryParseMonth(string? value, out DateOnly month) =>
        DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out month)
        && month.Year is >= 1900 and <= 9998;

    public static string DefaultRole(Account account) => AccountPreferences.EffectiveType(account) switch
    {
        "savings" or "term-deposit" => "savings",
        "home-loan" or "loan" or "investment" => "excluded",
        _ => "spending"
    };

    public async Task<HomeLoanReport> GetReport(Guid tenantId, int months, DateOnly? endMonth, CancellationToken cancellationToken) =>
        (await Build(tenantId, months, endMonth, cancellationToken)).Report;

    public async Task<(HomeLoanReport Report, IReadOnlyList<HomeLoanRow> Rows)> GetReportWithRows(Guid tenantId, int months, DateOnly? endMonth, CancellationToken cancellationToken)
    {
        var (report, rows) = await Build(tenantId, months, endMonth, cancellationToken);
        return (report, rows);
    }

    private async Task<(HomeLoanReport Report, List<HomeLoanRow> Rows)> Build(Guid tenantId, int months, DateOnly? endMonth, CancellationToken cancellationToken)
    {
        var calendar = await calendars.For(tenantId, cancellationToken);
        var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.CustomName ?? x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var profile = await dbContext.HomeLoanProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        var roles = accounts.ToDictionary(x => x.Id, x => profile is null ? DefaultRole(x)
            : profile.SpendingAccountIds.Contains(x.Id) ? "spending" : profile.SavingsAccountIds.Contains(x.Id) ? "savings" : "excluded");
        var names = accounts.ToDictionary(x => x.Id, AccountPreferences.DisplayName);
        var scope = accounts.Where(x => roles[x.Id] != "excluded").ToList();
        var scopeIds = scope.Select(x => x.Id).ToArray();
        var currency = AccountPreferences.AnalyticsCurrency(scope.Count > 0 ? scope : accounts, null);
        var first = profile?.FirstApplicant ?? DefaultFirstApplicant;
        var second = profile is null ? DefaultSecondApplicant : profile.SecondApplicant;

        var posted = dbContext.Transactions.AsNoTracking().Where(x => x.TenantId == tenantId && scopeIds.Contains(x.AccountId)
            && x.PostedAt != null && x.Currency == currency && (x.Status == null || x.Status == "" || x.Status.ToLower() == "posted"));
        var earliest = await posted.Select(x => x.PostedAt).MinAsync(cancellationToken);
        var latest = await posted.Select(x => x.PostedAt).MaxAsync(cancellationToken);
        var today = calendar.Today;
        var latestDate = latest is { } latestAt ? calendar.ToDate(latestAt) : today.AddMonths(-1);
        var latestMonth = new DateOnly(latestDate.Year, latestDate.Month, 1);
        var end = endMonth ?? (latest is not null && latestDate == latestMonth.AddMonths(1).AddDays(-1) ? latestMonth : latestMonth.AddMonths(-1));
        var start = end.AddMonths(1 - months);
        var monthKeys = Enumerable.Range(0, months).Select(x => FinancialCalendar.MonthKey(start.AddMonths(x))).ToList();
        var availableMonths = new List<string>();
        if (earliest is { } earliestAt)
        {
            var earliestDate = calendar.ToDate(earliestAt);
            for (var month = new DateOnly(earliestDate.Year, earliestDate.Month, 1); month <= latestMonth && availableMonths.Count < 120; month = month.AddMonths(1))
            {
                availableMonths.Add(FinancialCalendar.MonthKey(month));
            }
        }
        if (!availableMonths.Contains(FinancialCalendar.MonthKey(end)))
        {
            availableMonths.Add(FinancialCalendar.MonthKey(end));
            availableMonths.Sort(StringComparer.Ordinal);
        }

        var from = calendar.StartOf(start);
        var to = calendar.StartOf(end.AddMonths(1));
        var transactions = await posted.Where(x => x.PostedAt >= from && x.PostedAt < to)
            .OrderBy(x => x.PostedAt).ThenBy(x => x.Id)
            .Select(x => new SourceRow
            {
                Id = x.Id, AccountId = x.AccountId, Description = x.Description, MerchantName = x.MerchantName, Amount = x.Amount,
                PostedAt = x.PostedAt!.Value, TransferAccountId = x.InternalTransferAccountId,
                Tags = x.TagAssignments.Where(y => y.Tag != null && y.Tag.TenantId == tenantId)
                    .Select(y => new TagRef(y.TagId, y.Tag!.Name)).ToList()
            })
            .ToListAsync(cancellationToken);
        var sources = await dbContext.HomeLoanIncomeSources.AsNoTracking().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.MatchKey, cancellationToken);
        var choices = await dbContext.HomeLoanCategoryChoices.AsNoTracking().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.TagId, x => x.Category, cancellationToken);
        var tags = await dbContext.TransactionTags.AsNoTracking().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, cancellationToken);
        var vendors = await dbContext.MerchantVendors.AsNoTracking().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.MatchKey, x => x.Vendor, cancellationToken);

        var totals = monthKeys.Select(_ => new MonthTotals()).ToArray();
        var categories = new Dictionary<string, decimal[]>();
        var tagTotals = new Dictionary<Guid, decimal>();
        var payers = new Dictionary<string, PayerTotals>();
        var needsLabel = new Dictionary<string, MerchantVariant>();
        var flags = new Dictionary<string, List<HomeLoanFlagItem>>();
        var coverage = scope.ToDictionary(x => x.Id, _ => new HashSet<int>());
        var rows = new List<HomeLoanRow>();

        foreach (var transaction in transactions)
        {
            var date = calendar.ToDate(transaction.PostedAt);
            var index = (date.Year - start.Year) * 12 + date.Month - start.Month;
            if (index < 0 || index >= months)
            {
                continue;
            }
            var month = totals[index];
            var role = roles[transaction.AccountId];
            var account = names[transaction.AccountId];
            var description = transaction.Description ?? transaction.MerchantName ?? "";
            coverage[transaction.AccountId].Add(index);
            if (role == "savings")
            {
                month.Saved += transaction.Amount;
            }
            string type;
            string? applicant = null;
            string? category = null;
            string? flag = null;
            if (transaction.TransferAccountId is { } otherId)
            {
                var otherRole = roles.GetValueOrDefault(otherId, "excluded");
                type = otherRole == "excluded" ? "Transfer to or from another of your accounts"
                    : role == "savings" || otherRole == "savings" ? "Savings transfer" : "Transfer between your accounts";
            }
            else if (transaction.Amount > 0)
            {
                var key = HomeLoanCatalog.PayerKey(transaction.MerchantName, transaction.Description);
                var source = sources.GetValueOrDefault(key);
                if (!payers.TryGetValue(key, out var payer))
                {
                    payer = new PayerTotals(StatementNameCleaner.Clean(string.IsNullOrWhiteSpace(transaction.MerchantName) ? transaction.Description : transaction.MerchantName),
                        HomeLoanCatalog.Describe(transaction.MerchantName, transaction.Description).PaymentType);
                    payers[key] = payer;
                }
                payer.Count++;
                payer.Total += transaction.Amount;
                payer.Months.Add(index);
                payer.Latest = date;
                payer.Payments.Add(new HomeLoanPayment(transaction.Id, date, account, description, transaction.Amount));
                var amount = transaction.Amount;
                var person = source?.Applicant == 2 && second is not null ? 2 : source?.Applicant == 1 || source?.Kind == "salary" ? 1 : 0;
                switch (source?.Kind)
                {
                    case "salary" or "other-income":
                        if (person == 2) { month.Second += amount; applicant = second; }
                        else if (person == 1) { month.First += amount; applicant = first; }
                        else { month.Other += amount; }
                        type = source.Kind == "salary" ? "Salary" : "Other income";
                        break;
                    case "refund":
                        month.Refunds += amount;
                        type = "Refund";
                        break;
                    case "gift":
                        month.Gifts += amount;
                        type = "Gift";
                        break;
                    case "not-income":
                        type = "Not income";
                        break;
                    default:
                        month.UnlabelledCredits += amount;
                        type = "Credit not yet labelled";
                        break;
                }
                if (amount >= HomeLoanCatalog.LargeDepositThreshold && source?.Kind is null or "gift" or "other-income")
                {
                    flag = "Large one-off deposit";
                    Flag(flags, "large-deposit", new HomeLoanFlagItem(date, account, description, amount));
                }
            }
            else if (transaction.Amount < 0)
            {
                var spend = -transaction.Amount;
                type = "Spending";
                var allocations = new List<(string Key, decimal Amount)>();
                if (transaction.Tags.Count == 0)
                {
                    allocations.Add((HomeLoanCatalog.Unlabelled, spend));
                    var ruleName = StatementNameCleaner.Clean(string.IsNullOrWhiteSpace(transaction.MerchantName) ? transaction.Description : transaction.MerchantName);
                    var name = HomeLoanCatalog.Describe(transaction.MerchantName, transaction.Description);
                    if (!needsLabel.TryGetValue(ruleName, out var merchant))
                    {
                        merchant = new MerchantVariant(name);
                        needsLabel[ruleName] = merchant;
                    }
                    merchant.Add(spend, new HomeLoanPayment(transaction.Id, date, account, description, transaction.Amount));
                }
                else
                {
                    var share = decimal.Round(spend / transaction.Tags.Count, 2);
                    for (var i = 0; i < transaction.Tags.Count; i++)
                    {
                        var tag = transaction.Tags[i];
                        var part = i == 0 ? spend - share * (transaction.Tags.Count - 1) : share;
                        tagTotals[tag.Id] = tagTotals.GetValueOrDefault(tag.Id) + part;
                        allocations.Add((choices.GetValueOrDefault(tag.Id) ?? HomeLoanCatalog.DefaultCategory(tag.Name), part));
                    }
                }
                foreach (var (key, part) in allocations)
                {
                    if (!categories.TryGetValue(key, out var byMonth))
                    {
                        byMonth = new decimal[months];
                        categories[key] = byMonth;
                    }
                    byMonth[index] += part;
                    var group = key == HomeLoanCatalog.Unlabelled ? HomeLoanCatalog.Living : HomeLoanCatalog.Category(key)?.Group ?? HomeLoanCatalog.Living;
                    if (group == HomeLoanCatalog.Living)
                    {
                        month.Living += part;
                    }
                    else if (group == HomeLoanCatalog.Commitment)
                    {
                        month.Commitments += part;
                    }
                    else if (group == HomeLoanCatalog.Savings)
                    {
                        month.Saved += part;
                    }
                }
                category = string.Join(", ", allocations.Select(x => CategoryLabel(x.Key)).Distinct());
                if (allocations.All(x => HomeLoanCatalog.Category(x.Key)?.Group == HomeLoanCatalog.Savings))
                {
                    type = "Moved to savings";
                }
                if (HomeLoanCatalog.DebitFlag(transaction.MerchantName, transaction.Description) is { } rule)
                {
                    flag = rule.Label;
                    Flag(flags, rule.Kind, new HomeLoanFlagItem(date, account, description, transaction.Amount));
                }
            }
            else
            {
                type = "Zero amount";
            }
            rows.Add(new HomeLoanRow(date, account, RoleLabel(role), description, transaction.Amount, type, applicant, category,
                string.Join(", ", transaction.Tags.Select(x => x.Name)), flag));
        }

        var monthly = monthKeys.Select((key, i) => totals[i].ToMonth(key)).ToList();
        var total = Sum("Total", monthly, 1);
        var average = Sum("Monthly average", monthly, months);
        var categoryTotals = categories
            .Select(x => new HomeLoanCategoryTotal(x.Key, CategoryLabel(x.Key), x.Key == HomeLoanCatalog.Unlabelled ? HomeLoanCatalog.Living : HomeLoanCatalog.Category(x.Key)?.Group ?? HomeLoanCatalog.Living,
                x.Value.Sum(), decimal.Round(x.Value.Sum() / months, 2), x.Value))
            .OrderBy(x => x.Group == HomeLoanCatalog.Living ? 0 : x.Group == HomeLoanCatalog.Commitment ? 1 : x.Group == HomeLoanCatalog.Savings ? 2 : 3)
            .ThenBy(x => x.Key == HomeLoanCatalog.Unlabelled ? 1 : 0).ThenByDescending(x => x.Total).ToList();
        var tagChoices = tagTotals.Where(x => tags.ContainsKey(x.Key)).Select(x =>
        {
            var tag = tags[x.Key];
            var chosen = choices.GetValueOrDefault(x.Key);
            return new HomeLoanTagChoice(tag.Id, tag.Name, tag.Color, chosen ?? HomeLoanCatalog.DefaultCategory(tag.Name), chosen is null, x.Value);
        }).OrderByDescending(x => x.Total).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var payerList = payers.Select(x =>
        {
            var source = sources.GetValueOrDefault(x.Key);
            return new HomeLoanPayer(x.Key, source?.Name ?? x.Value.Name, x.Value.PaymentType, source?.Kind, source?.Applicant, x.Value.Count, x.Value.Total, x.Value.Months.Count, x.Value.Latest,
                x.Value.Payments.OrderByDescending(y => y.Date).Take(24).ToList());
        }).OrderBy(x => x.Kind is null ? 0 : 1).ThenByDescending(x => x.Total).Take(150).ToList();
        var flagList = flags.Select(x =>
        {
            var rule = HomeLoanCatalog.FlagRules.FirstOrDefault(y => y.Kind == x.Key);
            return new HomeLoanFlag(x.Key, rule?.Label ?? "Large one-off deposits",
                rule?.Advice ?? "Lenders ask where large deposits came from. Gifts and windfalls usually don't count as genuine savings.",
                x.Value.Count, x.Value.Sum(y => y.Amount), x.Value.OrderByDescending(y => y.Date).Take(50).ToList());
        }).OrderBy(x => x.Label).ToList();
        var gaps = scope.Select(x => new HomeLoanGap(x.Id, names[x.Id],
                monthKeys.Where((_, i) => !coverage[x.Id].Contains(i)).ToList()))
            .Where(x => x.MissingMonths.Count > 0).ToList();
        var setup = new HomeLoanSetup(first, second,
            accounts.Select(x => new HomeLoanAccountRole(x.Id, names[x.Id], AccountPreferences.EffectiveType(x), roles[x.Id])).ToList(),
            profile?.Version ?? 0, profile is not null);
        var report = new HomeLoanReport(currency, months, monthKeys[0], monthKeys[^1], availableMonths, setup, monthly, average, total,
            categoryTotals, tagChoices, payerList,
            Cluster(needsLabel, vendors).OrderByDescending(x => x.Total).ThenBy(x => x.Name).ToList(),
            flagList, gaps);
        return (report, rows);
    }

    public static string CategoryLabel(string key) => key == HomeLoanCatalog.Unlabelled ? "Not yet labelled" : HomeLoanCatalog.Category(key)?.Label ?? key;

    private static string RoleLabel(string role) => role switch { "savings" => "Savings", "spending" => "Spending", _ => "Not included" };

    private static void Flag(Dictionary<string, List<HomeLoanFlagItem>> flags, string kind, HomeLoanFlagItem item)
    {
        if (!flags.TryGetValue(kind, out var items))
        {
            items = [];
            flags[kind] = items;
        }
        items.Add(item);
    }

    private static HomeLoanMonth Sum(string label, IReadOnlyList<HomeLoanMonth> months, int divisor)
    {
        decimal Value(Func<HomeLoanMonth, decimal> field) => decimal.Round(months.Sum(field) / divisor, 2);
        return new HomeLoanMonth(label, Value(x => x.FirstApplicantIncome), Value(x => x.SecondApplicantIncome), Value(x => x.OtherIncome),
            Value(x => x.UnlabelledCredits), Value(x => x.Refunds), Value(x => x.Gifts), Value(x => x.LivingExpenses),
            Value(x => x.Commitments), Value(x => x.Saved), Value(x => x.Surplus));
    }

    private sealed class SourceRow
    {
        public Guid Id { get; init; }
        public Guid AccountId { get; init; }
        public string? Description { get; init; }
        public string? MerchantName { get; init; }
        public decimal Amount { get; init; }
        public DateTimeOffset PostedAt { get; init; }
        public Guid? TransferAccountId { get; init; }
        public List<TagRef> Tags { get; init; } = [];
    }

    private sealed record TagRef(Guid Id, string Name);

    private static IEnumerable<HomeLoanMerchant> Cluster(Dictionary<string, MerchantVariant> variants, IReadOnlyDictionary<string, string> vendors)
    {
        var groups = new List<MerchantCluster>();
        foreach (var (rule, variant) in variants.OrderByDescending(x => x.Value.Name.Brand).ThenBy(x => x.Value.Name.Words.Count).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            var words = string.Join(' ', variant.Name.Words).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            MerchantCluster? group;
            if (vendors.TryGetValue(Tagging.MerchantTagMatcher.Normalize(rule), out var vendor))
            {
                var key = $"vendor:{vendor.ToLowerInvariant()}";
                group = groups.FirstOrDefault(x => x.Key == key);
                if (group is null)
                {
                    group = new MerchantCluster(key, vendor, true, []);
                    groups.Add(group);
                }
            }
            else if (variant.Name.Brand || variant.Name.GroupKey.StartsWith("transfer:", StringComparison.Ordinal))
            {
                group = groups.FirstOrDefault(x => x.Key == variant.Name.GroupKey);
                if (group is null)
                {
                    group = new MerchantCluster(variant.Name.GroupKey, variant.Name.Name, false, []);
                    groups.Add(group);
                }
            }
            else
            {
                group = words.Count == 1 ? groups.FirstOrDefault(x => x.Key.StartsWith($"brand:{words[0]} ", StringComparison.Ordinal)) : null;
                group ??= groups.FirstOrDefault(x => x.Key.StartsWith("word:", StringComparison.Ordinal) && x.Prefix.Count > 0 && x.Prefix[0] == words[0]
                    && CommonPrefix(x.Prefix, words) >= Math.Min(2, Math.Min(x.Prefix.Count, words.Count)));
                if (group is null)
                {
                    group = new MerchantCluster($"word:{string.Join(' ', words)}", variant.Name.OwnName, false, words);
                    groups.Add(group);
                }
                else if (group.Key.StartsWith("word:", StringComparison.Ordinal))
                {
                    group.Prefix = group.Prefix.Take(CommonPrefix(group.Prefix, words)).ToList();
                    group.Name = HomeLoanCatalog.TitleCase(group.Prefix.Take(3));
                }
            }
            group.Members.Add((rule, variant));
        }
        return groups.Select(x => x.ToMerchant());
    }

    private static int CommonPrefix(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        var length = 0;
        while (length < first.Count && length < second.Count && first[length] == second[length])
        {
            length++;
        }
        return length;
    }

    private sealed class MerchantVariant(HomeLoanMerchantName name)
    {
        public HomeLoanMerchantName Name { get; } = name;
        public int Count { get; private set; }
        public decimal Total { get; private set; }
        public List<HomeLoanPayment> Payments { get; } = [];

        public void Add(decimal amount, HomeLoanPayment payment)
        {
            Count++;
            Total += amount;
            Payments.Add(payment);
        }
    }

    private sealed class MerchantCluster(string key, string name, bool confirmed, List<string> prefix)
    {
        public string Key { get; } = key;
        public string Name { get; set; } = name;
        public bool Confirmed { get; } = confirmed;
        public List<string> Prefix { get; set; } = prefix;
        public List<(string Rule, MerchantVariant Variant)> Members { get; } = [];

        public HomeLoanMerchant ToMerchant()
        {
            var ordered = Members.OrderByDescending(x => x.Variant.Total).ThenBy(x => x.Rule, StringComparer.Ordinal).ToList();
            var types = Members.Select(x => x.Variant.Name.PaymentType).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList();
            var suggestion = ordered.Select(x => MerchantKeywordCatalog.Suggest(Tagging.MerchantTagMatcher.Normalize(x.Rule))?.TagName)
                .FirstOrDefault(x => x is not null && x != "Transfers")
                ?? (types.Contains("Transfer") && Name.Contains("saving", StringComparison.OrdinalIgnoreCase) ? "Savings" : null);
            return new HomeLoanMerchant(Key, Name, Confirmed, types, suggestion, Members.Sum(x => x.Variant.Count), Members.Sum(x => x.Variant.Total),
                ordered.Select(x => new HomeLoanMerchantVariant(x.Rule, x.Variant.Name.OwnName, x.Variant.Count, x.Variant.Total)).ToList(),
                Members.SelectMany(x => x.Variant.Payments).OrderByDescending(x => x.Date).Take(30).ToList());
        }
    }
    private sealed class MonthTotals
    {
        public decimal First { get; set; }
        public decimal Second { get; set; }
        public decimal Other { get; set; }
        public decimal UnlabelledCredits { get; set; }
        public decimal Refunds { get; set; }
        public decimal Gifts { get; set; }
        public decimal Living { get; set; }
        public decimal Commitments { get; set; }
        public decimal Saved { get; set; }

        public HomeLoanMonth ToMonth(string key) => new(key, First, Second, Other, UnlabelledCredits, Refunds, Gifts, Living, Commitments, Saved,
            First + Second + Other - Living - Commitments);
    }

    private sealed class PayerTotals(string name, string? paymentType)
    {
        public string Name { get; } = name;
        public string? PaymentType { get; } = paymentType;
        public List<HomeLoanPayment> Payments { get; } = [];
        public int Count { get; set; }
        public decimal Total { get; set; }
        public HashSet<int> Months { get; } = [];
        public DateOnly Latest { get; set; }
    }
}
