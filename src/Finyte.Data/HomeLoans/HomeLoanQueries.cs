using System.Globalization;
using Finyte.Core.Accounts;
using Finyte.Core.HomeLoans;
using Finyte.Core.Recurring;
using Finyte.Core.Scheduling;
using Finyte.Data.Tagging;
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

    public static string PayerKey(string? statementName)
    {
        var key = MerchantTagMatcher.Normalize(statementName);
        return key.Length > 200 ? key[..200].TrimEnd() : key;
    }

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
        var sources = await dbContext.HomeLoanIncomeSources.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var choices = (await dbContext.HomeLoanCategoryChoices.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken))
            .Where(x => HomeLoanCatalog.Category(x.Category) is not null).ToDictionary(x => x.TagId, x => x.Category);
        var own = (await dbContext.HomeLoanPaymentCategories.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken))
            .Where(x => HomeLoanCatalog.Category(x.Category) is not null).ToDictionary(x => x.TransactionId, x => x.Category);
        var tags = await dbContext.TransactionTags.AsNoTracking().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, cancellationToken);

        var totals = monthKeys.Select(_ => new MonthTotals()).ToArray();
        var categories = new Dictionary<string, decimal[]>();
        var tagPayments = new Dictionary<Guid, List<HomeLoanPayment>>();
        var payers = new Dictionary<string, PayerTotals>();
        var matches = new Dictionary<string, HomeLoanIncomeSource?>();
        var review = new List<HomeLoanReviewPayment>();
        var untaggedCount = 0;
        var untaggedTotal = 0m;
        var deposits = new List<HomeLoanFlagItem>();
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
                var statement = StatementNameCleaner.Clean(string.IsNullOrWhiteSpace(transaction.MerchantName) ? transaction.Description : transaction.MerchantName);
                var key = PayerKey(statement);
                if (!matches.TryGetValue(key, out var source))
                {
                    source = sources.Where(x => key == x.MatchKey || key.StartsWith($"{x.MatchKey} ", StringComparison.Ordinal))
                        .OrderByDescending(x => x.MatchKey.Length).FirstOrDefault();
                    matches[key] = source;
                }
                if (!payers.TryGetValue(key, out var payer))
                {
                    payer = new PayerTotals(statement);
                    payers[key] = payer;
                }
                payer.Count++;
                payer.Total += transaction.Amount;
                payer.Months.Add(index);
                payer.Latest = date;
                payer.Payments.Add(new HomeLoanPayment(transaction.Id, date, account, description, transaction.Amount, null));
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
                    deposits.Add(new HomeLoanFlagItem(date, account, description, amount));
                }
            }
            else if (transaction.Amount < 0)
            {
                var spend = -transaction.Amount;
                var ownCategory = own.GetValueOrDefault(transaction.Id);
                var mapped = transaction.Tags.Select(x => choices.GetValueOrDefault(x.Id)).OfType<string>().Distinct().ToList();
                var key = ownCategory ?? (transaction.Tags.Count == 0 ? HomeLoanCatalog.Untagged
                    : mapped.Count == 1 ? mapped[0] : mapped.Count == 0 ? HomeLoanCatalog.Unmapped : HomeLoanCatalog.Review);
                foreach (var tag in transaction.Tags)
                {
                    if (!tagPayments.TryGetValue(tag.Id, out var list))
                    {
                        list = [];
                        tagPayments[tag.Id] = list;
                    }
                    list.Add(new HomeLoanPayment(transaction.Id, date, account, description, transaction.Amount, ownCategory));
                }
                if (key == HomeLoanCatalog.Untagged)
                {
                    untaggedCount++;
                    untaggedTotal += spend;
                }
                if (ownCategory is not null || key == HomeLoanCatalog.Review)
                {
                    review.Add(new HomeLoanReviewPayment(transaction.Id, date, account, description, transaction.Amount,
                        transaction.Tags.Select(x => x.Name).ToList(), ownCategory));
                }
                if (!categories.TryGetValue(key, out var byMonth))
                {
                    byMonth = new decimal[months];
                    categories[key] = byMonth;
                }
                byMonth[index] += spend;
                var group = HomeLoanCatalog.Group(key);
                if (group == HomeLoanCatalog.Commitment)
                {
                    month.Commitments += spend;
                }
                else if (group == HomeLoanCatalog.Savings)
                {
                    month.Saved += spend;
                }
                else
                {
                    month.Living += spend;
                }
                type = group == HomeLoanCatalog.Savings ? "Moved to savings" : "Spending";
                category = HomeLoanCatalog.Label(key);
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
            .Select(x => new HomeLoanCategoryTotal(x.Key, HomeLoanCatalog.Label(x.Key), HomeLoanCatalog.Group(x.Key), x.Value.Sum(),
                decimal.Round(x.Value.Sum() / months, 2), x.Value))
            .OrderBy(x => x.Group == HomeLoanCatalog.Living ? 0 : x.Group == HomeLoanCatalog.Commitment ? 1 : 2)
            .ThenBy(x => HomeLoanCatalog.Category(x.Key) is null ? 1 : 0).ThenByDescending(x => x.Total).ToList();
        var tagChoices = tagPayments.Where(x => tags.ContainsKey(x.Key)).Select(x =>
        {
            var tag = tags[x.Key];
            return new HomeLoanTagChoice(tag.Id, tag.Name, tag.Color, choices.GetValueOrDefault(x.Key), -x.Value.Sum(y => y.Amount),
                x.Value.OrderByDescending(y => y.Date).ToList());
        }).OrderBy(x => x.Category is null ? 0 : 1).ThenByDescending(x => x.Total).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var payerList = payers.Select(x =>
        {
            var source = matches.GetValueOrDefault(x.Key);
            return new HomeLoanPayer(x.Key, x.Value.Name, source?.MatchKey, source?.Kind, source?.Applicant, x.Value.Count, x.Value.Total, x.Value.Months.Count,
                x.Value.Latest, x.Value.Payments.OrderByDescending(y => y.Date).Take(24).ToList());
        }).OrderBy(x => x.Kind is null ? 0 : 1).ThenByDescending(x => x.Total).Take(150).ToList();
        IReadOnlyList<HomeLoanFlag> flagList = deposits.Count == 0 ? [] :
        [
            new HomeLoanFlag("large-deposit", "Large one-off deposits",
                "Lenders ask where large deposits came from. Gifts and windfalls usually don't count as genuine savings.",
                deposits.Count, deposits.Sum(x => x.Amount), deposits.OrderByDescending(x => x.Date).Take(50).ToList())
        ];
        var gaps = scope.Select(x => new HomeLoanGap(x.Id, names[x.Id], monthKeys.Where((_, i) => !coverage[x.Id].Contains(i)).ToList()))
            .Where(x => x.MissingMonths.Count > 0).ToList();
        var setup = new HomeLoanSetup(first, second,
            accounts.Select(x => new HomeLoanAccountRole(x.Id, names[x.Id], AccountPreferences.EffectiveType(x), roles[x.Id])).ToList(),
            profile?.Version ?? 0, profile is not null);
        var report = new HomeLoanReport(currency, months, monthKeys[0], monthKeys[^1], availableMonths, setup, monthly, average, total,
            categoryTotals, tagChoices, payerList, new HomeLoanUntagged(untaggedCount, untaggedTotal),
            review.OrderBy(x => x.OwnCategory is null ? 0 : 1).ThenByDescending(x => x.Date).ToList(), flagList, gaps, HomeLoanCatalog.Categories);
        return (report, rows);
    }

    private static string RoleLabel(string role) => role switch { "savings" => "Savings", "spending" => "Spending", _ => "Not included" };

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

    private sealed class PayerTotals(string name)
    {
        public string Name { get; } = name;
        public List<HomeLoanPayment> Payments { get; } = [];
        public int Count { get; set; }
        public decimal Total { get; set; }
        public HashSet<int> Months { get; } = [];
        public DateOnly Latest { get; set; }
    }
}
