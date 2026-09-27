using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Recurring;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finyte.Data.Recurring;

public sealed class RecurringPaymentService(FinyteDbContext dbContext, TimeProvider timeProvider)
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
    private static DateOnly MinimumDate { get; } = new(1900, 1, 1);
    private static DateOnly MaximumDate { get; } = new(9998, 12, 31);

    public async Task<RecurringSeriesList> List(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null)
    {
        ValidateRange(from, to);
        await using var snapshot = await ReadSnapshot(cancellationToken);
        var series = await dbContext.RecurringPaymentSeries.AsNoTracking().Include(x => x.Aliases)
            .Where(x => x.TenantId == tenantId && (accountId == null || x.AccountId == accountId)).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var items = new List<RecurringSeriesResponse>();
        foreach (var item in series)
        {
            items.Add(await Summary(item, from, to, cancellationToken));
        }
        var costs = series.Where(x => x.State == "active").GroupBy(x => x.Currency)
            .Select(x => new RecurringCostSummary(x.Key, decimal.Round(x.Sum(y => AnnualCost(y)) / 12, 2),
                decimal.Round(x.Sum(y => AnnualCost(y)), 2), x.Count(), x.Count(y => y.AmountMode == "variable"),
                decimal.Round(x.Where(y => y.Kind == "subscription").Sum(y => AnnualCost(y)) / 12, 2),
                decimal.Round(x.Where(y => y.Kind == "bill").Sum(y => AnnualCost(y)) / 12, 2)))
            .OrderBy(x => x.Currency).ToList();
        return new RecurringSeriesList(items, costs, from, to);
    }

    public async Task<RecurringDiscoveryPage> Discover(Guid tenantId, DateOnly from, DateOnly to, int page, int pageSize, bool dismissed, CancellationToken cancellationToken, Guid? accountId = null, string? search = null, string? cadence = null, string? sort = null, bool hideEnded = false)
    {
        ValidateRange(from, to);
        ValidatePage(page, pageSize);
        await using var snapshot = await ReadSnapshot(cancellationToken);
        var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        if (accountId.HasValue && !accounts.Any(x => x.Id == accountId.Value))
        {
            throw new KeyNotFoundException("Account not found.");
        }
        if (search?.Length > 120 || (sort is not null and not "name" and not "amount")
            || (cadence is not null && !new[] { "weekly", "fortnightly", "monthly", "quarterly", "yearly" }.Contains(cadence)))
        {
            throw new ArgumentException("Choose a valid discovery filter.");
        }
        var accountIds = accounts.Where(x => accountId.HasValue ? x.Id == accountId.Value : AccountPreferences.IncludeInAnalytics(x)).Select(x => x.Id).ToArray();
        var query = Eligible(tenantId).Include(x => x.Account).Where(x => accountIds.Contains(x.AccountId) && x.PostedAt >= Timestamp(from) && x.PostedAt < Timestamp(to.AddDays(1))
            && !dbContext.RecurringPaymentDecisions.Any(y => y.TenantId == tenantId && y.TransactionId == x.Id && y.Status == "confirmed"));
        if (await query.CountAsync(cancellationToken) > 10000)
        {
            throw new ArgumentException("This discovery range contains more than 10,000 eligible transactions. Narrow the history range; no transactions have been silently omitted.");
        }
        var rows = await query.OrderBy(x => x.PostedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var decisions = await dbContext.RecurringDiscoveryDecisions.AsNoTracking().Where(x => x.TenantId == tenantId)
            .Select(x => x.CandidateKey).ToHashSetAsync(cancellationToken);
        var patterns = RecurringPatternDetector.Detect(rows.Select(x => new RecurringPatternTransaction(x.Id, x.AccountId, x.Currency,
            x.Amount, DateOnly.FromDateTime(x.PostedAt!.Value.UtcDateTime), x.MerchantName, x.Description, x.Reference)).ToList(),
            new RecurringDetectionOptions(IncludeEarly: true, AsOf: to < Today ? to : Today));
        var filtered = patterns.Where(x => decisions.Contains(x.Key) == dismissed
            && (!hideEnded || !x.IsEnded)
            && (string.IsNullOrWhiteSpace(search) || x.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            && (cadence is null || x.Cadence == cadence));
        var candidates = (sort == "amount"
            ? filtered.OrderBy(x => x.IsEnded).ThenBy(x => x.Currency).ThenByDescending(x => x.ExpectedAmount).ThenBy(x => x.Key)
            : filtered.OrderBy(x => x.IsEnded).ThenBy(x => x.Name).ThenBy(x => x.Key)).ToList();
        var byId = rows.ToDictionary(x => x.Id);
        var items = candidates.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new RecurringDiscoveryResponse(x.Key,
            x.Name, x.AccountId, AccountName(byId[x.TransactionIds[0]]), x.Currency, x.Cadence, x.AnchorDate, x.ExpectedAmount,
            x.AliasField, x.AliasValue, x.TransactionIds.Select(y => new RecurringDiscoveryTransaction(Evidence(byId[y]),
                RecurringCalendar.Resolve(x.Cadence, x.AnchorDate, DateOnly.FromDateTime(byId[y].PostedAt!.Value.UtcDateTime)).Date)).ToList(), x.Evidence, dismissed, x.IsEarly, x.IsEnded, SuggestedKind(x.AliasValue))).ToList();
        return new RecurringDiscoveryPage(items, candidates.Count, page, pageSize, from, to);
    }

    public async Task DiscoveryDecision(Guid tenantId, RecurringDiscoveryDecisionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateKey) || request.CandidateKey.Length > 256 || request.Action is not "dismiss" and not "reset")
        {
            throw new ArgumentException("Provide a discovery key and dismiss or reset action.");
        }
        await using var transaction = await WriteLock(tenantId, [], cancellationToken);
        var decision = await dbContext.RecurringDiscoveryDecisions.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.CandidateKey == request.CandidateKey, cancellationToken);
        if (request.Action == "reset" && decision is not null)
        {
            dbContext.RecurringDiscoveryDecisions.Remove(decision);
        }
        else if (request.Action == "dismiss" && decision is null)
        {
            dbContext.RecurringDiscoveryDecisions.Add(new RecurringDiscoveryDecision { TenantId = tenantId, CandidateKey = request.CandidateKey, DismissedAt = timeProvider.GetUtcNow() });
        }
        await Save(transaction, cancellationToken);
    }

    public async Task<RecurringSeriesResponse> Create(Guid tenantId, string userId, CreateRecurringRequest request, CancellationToken cancellationToken)
    {
        ValidateSettings(request.Name, request.Cadence, request.AnchorDate, request.ExpectedAmount, request.AmountMode, "active");
        ValidateKind(request.Kind);
        if (request.AccountId == Guid.Empty || string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Length != 3
            || !request.Currency.All(x => char.IsAsciiLetter(x)) || request.History?.Count > 300 || request.Aliases?.Count > 20
            || request.History?.Any(x => x is null) == true || request.Aliases?.Any(x => x is null) == true)
        {
            throw new ArgumentException("Provide an account, three-letter currency, at most 20 aliases and at most 300 selected history transactions.");
        }
        var history = request.History ?? [];
        if (history.Select(x => x.TransactionId).Distinct().Count() != history.Count || history.Select(x => x.OccurrenceDate).Distinct().Count() != history.Count)
        {
            throw new ArgumentException("Choose at most one transaction for each occurrence; history transactions must be unique.");
        }
        await using var transaction = await WriteLock(tenantId, history.Select(x => x.TransactionId).ToArray(), cancellationToken);
        if (await dbContext.RecurringPaymentSeries.CountAsync(x => x.TenantId == tenantId, cancellationToken) >= 200)
        {
            throw new ArgumentException("A family can track up to 200 recurring series.");
        }
        var currency = request.Currency.ToUpperInvariant();
        var account = await dbContext.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AccountId && x.Currency == currency, cancellationToken)
            ?? throw new ArgumentException("Select an account in this family using the chosen currency.");
        var series = new RecurringPaymentSeries
        {
            TenantId = tenantId, AccountId = account.Id, Currency = currency, Name = request.Name.Trim(), Cadence = request.Cadence,
            AnchorDate = request.AnchorDate, ExpectedAmount = request.ExpectedAmount, AmountMode = request.AmountMode,
            Kind = request.Kind ?? "subscription", CreatedAt = timeProvider.GetUtcNow(), UpdatedAt = timeProvider.GetUtcNow()
        };
        foreach (var alias in request.Aliases ?? [])
        {
            AddAlias(series, alias.Field, alias.Value);
        }
        dbContext.RecurringPaymentSeries.Add(series);
        foreach (var item in history)
        {
            await ApplyDecision(series, userId, new RecurringDecisionRequest(item.TransactionId, item.OccurrenceDate, "confirm", 0, item.Fingerprint, null), cancellationToken);
        }
        await Save(transaction, cancellationToken);
        return await Summary(series, DefaultFrom(), DefaultTo(), cancellationToken);
    }

    public async Task<RecurringSeriesResponse> Update(Guid tenantId, Guid seriesId, UpdateRecurringRequest request, CancellationToken cancellationToken)
    {
        ValidateSettings(request.Name, request.Cadence, request.AnchorDate, request.ExpectedAmount, request.AmountMode, request.State);
        ValidateKind(request.Kind);
        await using var transaction = await WriteLock(tenantId, [], cancellationToken);
        var series = await Find(tenantId, seriesId, cancellationToken);
        CheckVersion(series, request.ExpectedVersion);
        series.Name = request.Name.Trim();
        series.Cadence = request.Cadence;
        series.AnchorDate = request.AnchorDate;
        series.ExpectedAmount = request.ExpectedAmount;
        series.AmountMode = request.AmountMode;
        series.State = request.State;
        series.Kind = request.Kind ?? series.Kind;
        Changed(series);
        await Save(transaction, cancellationToken);
        return await Summary(series, DefaultFrom(), DefaultTo(), cancellationToken);
    }

    public async Task<RecurringSeriesResponse> Decide(Guid tenantId, string userId, Guid seriesId, RecurringDecisionRequest request, CancellationToken cancellationToken)
    {
        if (request.Action is not "confirm" and not "reject" and not "reset")
        {
            throw new ArgumentException("Use confirm, reject or reset.");
        }
        await using var transaction = await WriteLock(tenantId, [request.TransactionId], cancellationToken);
        var series = await Find(tenantId, seriesId, cancellationToken);
        CheckVersion(series, request.ExpectedVersion);
        await ApplyDecision(series, userId, request, cancellationToken);
        Changed(series);
        await Save(transaction, cancellationToken);
        return await Summary(series, DefaultFrom(), DefaultTo(), cancellationToken);
    }

    public async Task<RecurringSeriesResponse> RemoveAlias(Guid tenantId, Guid seriesId, Guid aliasId, int? expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await WriteLock(tenantId, [], cancellationToken);
        var series = await Find(tenantId, seriesId, cancellationToken);
        CheckVersion(series, expectedVersion);
        var alias = series.Aliases.SingleOrDefault(x => x.Id == aliasId) ?? throw new KeyNotFoundException("Alias not found.");
        series.Aliases.Remove(alias);
        dbContext.RecurringPaymentAliases.Remove(alias);
        Changed(series);
        await Save(transaction, cancellationToken);
        return await Summary(series, DefaultFrom(), DefaultTo(), cancellationToken);
    }

    public async Task<RecurringOccurrencePage> Occurrences(Guid tenantId, Guid seriesId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ValidateRange(from, to);
        await using var snapshot = await ReadSnapshot(cancellationToken);
        var series = await Find(tenantId, seriesId, cancellationToken);
        return new RecurringOccurrencePage(await OccurrenceRows(series, from, to, cancellationToken), from, to);
    }

    public async Task<RecurringCandidatePage> Candidates(Guid tenantId, Guid seriesId, DateOnly from, DateOnly to, DateOnly? occurrenceDate,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        ValidateRange(from, to);
        ValidatePage(page, pageSize);
        await using var snapshot = await ReadSnapshot(cancellationToken);
        var series = await Find(tenantId, seriesId, cancellationToken);
        var query = Eligible(tenantId).Include(x => x.Account).Where(x => x.AccountId == series.AccountId && x.Currency == series.Currency
            && x.PostedAt >= Timestamp(from) && x.PostedAt < Timestamp(to.AddDays(1)));
        if (occurrenceDate is { } date)
        {
            ValidateOccurrence(series, date);
            var occurrence = RecurringCalendar.Resolve(series.Cadence, series.AnchorDate, date);
            query = query.Where(x => x.PostedAt >= Timestamp(occurrence.WindowFrom) && x.PostedAt < Timestamp(occurrence.WindowTo.AddDays(1)));
        }
        // Rank the complete review range before pagination. Include full neighbouring windows so
        // narrowing the visible dates cannot conceal a competing payment.
        var contextFrom = DateOnly.FromDayNumber(Math.Max(MinimumDate.DayNumber, from.DayNumber - 6));
        var contextTo = DateOnly.FromDayNumber(Math.Min(MaximumDate.DayNumber, to.DayNumber + 6));
        if (occurrenceDate is { } selectedDate)
        {
            var selected = RecurringCalendar.Resolve(series.Cadence, series.AnchorDate, selectedDate);
            contextFrom = selected.WindowFrom;
            contextTo = selected.WindowTo;
        }
        var contextQuery = Eligible(tenantId).Include(x => x.Account)
            .Where(x => x.AccountId == series.AccountId && x.Currency == series.Currency
                && x.PostedAt >= Timestamp(contextFrom) && x.PostedAt < Timestamp(contextTo.AddDays(1)));
        if (await contextQuery.CountAsync(cancellationToken) > 10000)
        {
            throw new ArgumentException("This review range contains more than 10,000 eligible transactions including neighbouring payment windows. Narrow the range; no candidates have been silently omitted.");
        }
        var rows = await contextQuery.OrderByDescending(x => x.PostedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var ids = rows.Select(x => x.Id).ToArray();
        var eligibleIds = ids.ToHashSet();
        var peers = await dbContext.RecurringPaymentSeries.AsNoTracking().Include(x => x.Aliases)
            .Where(x => x.TenantId == tenantId && x.AccountId == series.AccountId && x.Currency == series.Currency && x.State == "active")
            .ToListAsync(cancellationToken);
        var peerIds = peers.Select(x => x.Id).Append(seriesId).ToArray();
        var decisions = await dbContext.RecurringPaymentDecisions.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status != "reset"
            && (ids.Contains(x.TransactionId) || peerIds.Contains(x.SeriesId) && x.Status == "confirmed"
                && x.OccurrenceDate >= contextFrom && x.OccurrenceDate <= contextTo)).ToListAsync(cancellationToken);
        var rankings = RecurringCandidateRanker.Rank(series, peers, rows.Select(x => new RecurringPatternTransaction(x.Id,
            x.AccountId, x.Currency, x.Amount, DateOnly.FromDateTime(x.PostedAt!.Value.UtcDateTime), x.MerchantName, x.Description, x.Reference)).ToList(), decisions, occurrenceDate);
        var visibleIds = await query.Select(x => x.Id).ToHashSetAsync(cancellationToken);
        var items = rows.Where(x => visibleIds.Contains(x.Id)).Select(x =>
        {
            var actualDate = DateOnly.FromDateTime(x.PostedAt!.Value.UtcDateTime);
            var occurrence = RecurringCalendar.Resolve(series.Cadence, series.AnchorDate, occurrenceDate ?? actualDate);
            var aliasMatch = series.Aliases.Any(y => y.NormalizedValue == RecurringPatternDetector.NormalizeAlias(y.Field == "merchant" ? x.MerchantName : x.Description));
            var decision = decisions.SingleOrDefault(y => y.SeriesId == seriesId && y.TransactionId == x.Id && y.OccurrenceDate == occurrence.Date)
                ?? decisions.SingleOrDefault(y => y.SeriesId == seriesId && y.TransactionId == x.Id && y.Status == "confirmed");
            var reserved = decisions.SingleOrDefault(y => y.TransactionId == x.Id && y.Status == "confirmed");
            var ranking = rankings[x.Id];
            return new RecurringCandidateResponse(Evidence(x), occurrence.Date, aliasMatch, -x.Amount != series.ExpectedAmount, ranking.Reasons,
                decision?.Status == "confirmed" && !Valid(series, decision, x, eligibleIds) ? "needs-review" : decision?.Status, reserved?.SeriesId, ranking);
        }).OrderByDescending(x => x.Ranking.Confidence switch { "high" => 3, "medium" => 2, "ambiguous" => 1, _ => 0 })
            .ThenByDescending(x => x.Ranking.Score).ThenByDescending(x => x.Snapshot.PostedAt).ThenBy(x => x.Snapshot.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new RecurringCandidatePage(items, visibleIds.Count, page, pageSize, from, to);
    }

    public async Task<RecurringReviewPage> History(Guid tenantId, Guid seriesId, int page, int pageSize, CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        await using var snapshot = await ReadSnapshot(cancellationToken);
        var series = await Find(tenantId, seriesId, cancellationToken);
        var query = dbContext.RecurringPaymentReviews.AsNoTracking().Where(x => x.TenantId == tenantId && x.SeriesId == seriesId);
        var count = await query.CountAsync(cancellationToken);
        var reviews = await query.OrderByDescending(x => x.ReviewedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var ids = reviews.Select(x => x.TransactionId).ToArray();
        var transactions = await dbContext.Transactions.AsNoTracking().Include(x => x.Account).Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var decisions = await dbContext.RecurringPaymentDecisions.AsNoTracking().Where(x => x.TenantId == tenantId && x.SeriesId == seriesId && ids.Contains(x.TransactionId)).ToListAsync(cancellationToken);
        var eligibleIds = await Eligible(tenantId).Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToHashSetAsync(cancellationToken);
        var items = reviews.Select(x =>
        {
            transactions.TryGetValue(x.TransactionId, out var current);
            var decision = decisions.SingleOrDefault(y => y.TransactionId == x.TransactionId && y.OccurrenceDate == x.OccurrenceDate);
            var status = decision?.Status ?? "reset";
            if (status == "confirmed" && !Valid(series, decision!, current, eligibleIds))
            {
                status = "needs-review";
            }
            return new RecurringReviewResponse(x.Id, x.TransactionId, x.OccurrenceDate, x.Action,
                JsonSerializer.Deserialize<RecurringTransactionEvidence>(x.SnapshotJson, JsonOptions)!, status,
                current is null ? null : Evidence(current), x.ReviewedByUserId, x.ReviewedAt);
        }).ToList();
        return new RecurringReviewPage(items, count, page, pageSize);
    }

    public async Task<RecurringUpcomingPage> Upcoming(Guid tenantId, int days, Guid? accountId, CancellationToken cancellationToken)
    {
        if (days is < 1 or > 31)
        {
            throw new ArgumentException("Choose between 1 and 31 days.");
        }
        await using var snapshot = await ReadSnapshot(cancellationToken);
        var series = await dbContext.RecurringPaymentSeries.AsNoTracking().Include(x => x.Aliases)
            .Where(x => x.TenantId == tenantId && x.State == "active" && (accountId == null || x.AccountId == accountId)).ToListAsync(cancellationToken);
        var accountNames = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.CustomName ?? x.Name, cancellationToken);
        var items = new List<RecurringUpcomingItem>();
        foreach (var item in series)
        {
            foreach (var occurrence in await OccurrenceRows(item, Today, Today.AddDays(days - 1), cancellationToken))
            {
                if (occurrence.Status is "upcoming" or "due")
                {
                    items.Add(new RecurringUpcomingItem(item.Id, item.Name, item.Kind, item.AccountId,
                        accountNames.GetValueOrDefault(item.AccountId, "Account unavailable"), item.Currency, occurrence.Date, item.ExpectedAmount));
                }
            }
        }
        return new RecurringUpcomingPage(items.OrderBy(x => x.Date).ThenBy(x => x.Name).ToList(), series.Count);
    }

    private async Task ApplyDecision(RecurringPaymentSeries series, string userId, RecurringDecisionRequest request, CancellationToken cancellationToken)
    {
        var decision = await dbContext.RecurringPaymentDecisions.SingleOrDefaultAsync(x => x.TenantId == series.TenantId && x.SeriesId == series.Id
            && x.TransactionId == request.TransactionId && x.OccurrenceDate == request.OccurrenceDate, cancellationToken);
        var row = await dbContext.Transactions.Include(x => x.Account).SingleOrDefaultAsync(x => x.TenantId == series.TenantId && x.Id == request.TransactionId, cancellationToken);
        if (request.Action == "confirm" || decision is null)
        {
            ValidateOccurrence(series, request.OccurrenceDate);
            if (row is null || !await Eligible(series.TenantId).AnyAsync(x => x.Id == request.TransactionId && x.AccountId == series.AccountId && x.Currency == series.Currency, cancellationToken))
            {
                throw new ArgumentException("Only posted, dated debits on this series account and currency, through today and outside confirmed transfers, can be reviewed.");
            }
            if (request.Fingerprint != Fingerprint(row))
            {
                throw new RecurringConflictException("Transaction evidence changed. Reload and review the latest details.");
            }
        }
        if (request.Action == "confirm")
        {
            var occupied = await dbContext.RecurringPaymentDecisions.Where(x => x.TenantId == series.TenantId && x.Status == "confirmed"
                && (x.TransactionId == request.TransactionId || x.SeriesId == series.Id && x.OccurrenceDate == request.OccurrenceDate)).ToListAsync(cancellationToken);
            if (occupied.Any(x => x.SeriesId != series.Id || x.TransactionId != request.TransactionId))
            {
                throw new RecurringConflictException("This transaction or scheduled occurrence is already confirmed elsewhere. Unlink it before replacing that decision.");
            }
            foreach (var prior in occupied.Where(x => x.Id != decision?.Id))
            {
                prior.Status = "reset";
                prior.UpdatedAt = timeProvider.GetUtcNow();
                AddReview(series, userId, prior.TransactionId, prior.OccurrenceDate, "reassigned", prior.SnapshotJson);
            }
            // Flush released unique slots before assigning the same transaction to a new occurrence.
            if (occupied.Any(x => x.Id != decision?.Id))
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            if (request.LearnAliasField is not null)
            {
                AddAlias(series, request.LearnAliasField, request.LearnAliasField == "merchant" ? row!.MerchantName : row!.Description);
            }
        }
        else if (request.LearnAliasField is not null)
        {
            throw new ArgumentException("Only confirming a payment can explicitly approve a new alias.");
        }
        var snapshot = row is null ? decision!.SnapshotJson : JsonSerializer.Serialize(Evidence(row), JsonOptions);
        if (decision is null)
        {
            decision = new RecurringPaymentDecision
            {
                TenantId = series.TenantId, SeriesId = series.Id, TransactionId = request.TransactionId, OccurrenceDate = request.OccurrenceDate,
                Status = "reset", Fingerprint = row is null ? "" : Fingerprint(row), SnapshotJson = snapshot
            };
            dbContext.RecurringPaymentDecisions.Add(decision);
        }
        decision.Status = request.Action switch { "confirm" => "confirmed", "reject" => "rejected", _ => "reset" };
        decision.Fingerprint = row is null ? decision.Fingerprint : Fingerprint(row);
        decision.SnapshotJson = snapshot;
        decision.UpdatedAt = timeProvider.GetUtcNow();
        AddReview(series, userId, request.TransactionId, request.OccurrenceDate, request.Action, snapshot);
    }

    private void AddReview(RecurringPaymentSeries series, string userId, Guid transactionId, DateOnly date, string action, string snapshot)
    {
        dbContext.RecurringPaymentReviews.Add(new RecurringPaymentReview
        {
            TenantId = series.TenantId, SeriesId = series.Id, TransactionId = transactionId, OccurrenceDate = date,
            Action = action, SnapshotJson = snapshot, ReviewedByUserId = userId, ReviewedAt = timeProvider.GetUtcNow()
        });
    }

    private void AddAlias(RecurringPaymentSeries series, string field, string? value)
    {
        if (field is not "merchant" and not "description" || string.IsNullOrWhiteSpace(value) || value.Length > 512)
        {
            throw new ArgumentException("Approve a non-empty merchant or description alias, up to 512 characters.");
        }
        var normalized = RecurringPatternDetector.NormalizeAlias(value);
        if (normalized.Length is 0 or > 512)
        {
            throw new ArgumentException("An alias needs letters or digits and must normalize to at most 512 characters.");
        }
        if (!series.Aliases.Any(x => x.Field == field && x.NormalizedValue == normalized))
        {
            if (series.Aliases.Count >= 20)
            {
                throw new ArgumentException("A series can contain at most 20 approved aliases.");
            }
            var alias = new RecurringPaymentAlias { SeriesId = series.Id, Field = field, Value = value.Trim(), NormalizedValue = normalized, CreatedAt = timeProvider.GetUtcNow() };
            series.Aliases.Add(alias);
            dbContext.RecurringPaymentAliases.Add(alias);
        }
    }

    private async Task<RecurringSeriesResponse> Summary(RecurringPaymentSeries series, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var accountName = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == series.TenantId && x.Id == series.AccountId)
            .Select(x => x.CustomName ?? x.Name).SingleOrDefaultAsync(cancellationToken) ?? "Account unavailable";
        var occurrences = await OccurrenceRows(series, from, to, cancellationToken);
        var next = occurrences.FirstOrDefault(x => x.Status is "needs-review" or "no-payment-found" or "due" or "upcoming");
        var lastPaid = occurrences.Where(x => x.Status == "paid" && x.PaidAmount is not null).MaxBy(x => x.Date);
        var missed = series.State == "active"
            ? occurrences.Where(x => x.Status == "no-payment-found" && x.Date >= Today.AddDays(-45)).MaxBy(x => x.Date)?.Date
            : null;
        return new RecurringSeriesResponse(series.Id, series.Name, series.AccountId, accountName, series.Currency, series.Cadence,
            series.AnchorDate, series.ExpectedAmount, series.AmountMode, series.State, series.Version,
            series.Aliases.OrderBy(x => x.Field).ThenBy(x => x.Value).Select(x => new RecurringAliasResponse(x.Id, x.Field, x.Value)).ToList(),
            series.State == "active" ? next?.Date : null, series.State == "active" ? next?.Status ?? "none-in-range" : series.State,
            await NeedsReviewCount(series, cancellationToken), series.Kind, lastPaid?.PaidAmount, lastPaid?.Date,
            series.AmountMode == "fixed" && lastPaid?.PaidAmount is { } paid && paid != series.ExpectedAmount, missed);
    }

    private async Task<int> NeedsReviewCount(RecurringPaymentSeries series, CancellationToken cancellationToken)
    {
        var query = dbContext.RecurringPaymentDecisions.AsNoTracking().Where(x => x.TenantId == series.TenantId && x.SeriesId == series.Id && x.Status == "confirmed");
        var count = 0;
        var offset = 0;
        while (true)
        {
            var decisions = await query.OrderBy(x => x.Id).Skip(offset).Take(500).ToListAsync(cancellationToken);
            if (decisions.Count == 0)
            {
                return count;
            }
            var ids = decisions.Select(x => x.TransactionId).ToArray();
            var rows = await dbContext.Transactions.AsNoTracking().Where(x => x.TenantId == series.TenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            var eligible = await Eligible(series.TenantId).Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToHashSetAsync(cancellationToken);
            count += decisions.Count(x => !Valid(series, x, rows.GetValueOrDefault(x.TransactionId), eligible));
            offset += decisions.Count;
        }
    }

    private async Task<IReadOnlyList<RecurringOccurrenceResponse>> OccurrenceRows(RecurringPaymentSeries series, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var decisions = await dbContext.RecurringPaymentDecisions.AsNoTracking().Where(x => x.TenantId == series.TenantId && x.SeriesId == series.Id
            && x.Status == "confirmed" && x.OccurrenceDate >= from && x.OccurrenceDate <= to).ToListAsync(cancellationToken);
        var ids = decisions.Select(x => x.TransactionId).ToArray();
        var rows = await dbContext.Transactions.AsNoTracking().Where(x => x.TenantId == series.TenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var eligible = await Eligible(series.TenantId).Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToHashSetAsync(cancellationToken);
        var dates = new SortedSet<DateOnly>(decisions.Select(x => x.OccurrenceDate));
        var first = RecurringCalendar.Resolve(series.Cadence, series.AnchorDate, from);
        var index = first.Index;
        while (RecurringCalendar.Add(series.Cadence, series.AnchorDate, index) is { } date && date <= to)
        {
            if (date >= from && date >= series.AnchorDate)
            {
                dates.Add(date);
            }
            index++;
        }
        return dates.Select(x =>
        {
            var occurrence = RecurringCalendar.Resolve(series.Cadence, series.AnchorDate, x);
            var decision = decisions.SingleOrDefault(y => y.OccurrenceDate == x);
            Transaction? row = null;
            if (decision is not null)
            {
                rows.TryGetValue(decision.TransactionId, out row);
            }
            var valid = decision is not null && Valid(series, decision, row, eligible);
            var status = decision is not null ? valid ? "paid" : "needs-review"
                : series.State != "active" ? series.State : Today > occurrence.WindowTo ? "no-payment-found"
                : Today >= occurrence.WindowFrom ? "due" : "upcoming";
            return new RecurringOccurrenceResponse(x, occurrence.WindowFrom, occurrence.WindowTo, status, series.ExpectedAmount,
                valid ? -row!.Amount : null, decision?.TransactionId);
        }).ToList();
    }

    private bool Valid(RecurringPaymentSeries series, RecurringPaymentDecision decision, Transaction? row, HashSet<Guid> eligible) =>
        row is not null && eligible.Contains(row.Id) && row.AccountId == series.AccountId && row.Currency == series.Currency
        && Fingerprint(row) == decision.Fingerprint && decision.OccurrenceDate >= series.AnchorDate
        && RecurringCalendar.Resolve(series.Cadence, series.AnchorDate, decision.OccurrenceDate).Date == decision.OccurrenceDate;

    private IQueryable<Transaction> Eligible(Guid tenantId) => dbContext.Transactions.AsNoTracking().Where(x => x.TenantId == tenantId
        && x.Amount < 0 && x.PostedAt != null && x.PostedAt < Timestamp(Today.AddDays(1))
        && (x.Status == null || x.Status == "" || x.Status == "posted" || x.Status == "POSTED"))
        .ExcludeInternalTransfers(dbContext, tenantId);

    private async Task<RecurringPaymentSeries> Find(Guid tenantId, Guid seriesId, CancellationToken cancellationToken) =>
        await dbContext.RecurringPaymentSeries.Include(x => x.Aliases).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == seriesId, cancellationToken)
        ?? throw new KeyNotFoundException("Recurring series not found.");

    private async Task<IDbContextTransaction?> WriteLock(Guid tenantId, Guid[] transactionIds, CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.IsRelational() ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            if (dbContext.Database.IsNpgsql())
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM tenants WHERE \"Id\" = {tenantId} FOR UPDATE", cancellationToken);
                if (transactionIds.Length > 0)
                {
                    await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM transactions WHERE \"TenantId\" = {tenantId} AND \"Id\" = ANY ({transactionIds}) ORDER BY \"Id\" FOR UPDATE", cancellationToken);
                }
            }
            return transaction;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
            throw;
        }
    }

    private async Task<IDbContextTransaction?> ReadSnapshot(CancellationToken cancellationToken) => dbContext.Database.IsRelational()
        ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;

    private async Task Save(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private void Changed(RecurringPaymentSeries series)
    {
        series.Version++;
        series.UpdatedAt = timeProvider.GetUtcNow();
    }

    private static void CheckVersion(RecurringPaymentSeries series, int? expectedVersion)
    {
        if (expectedVersion is null or < 0)
        {
            throw new ArgumentException("The current series version is required.");
        }
        if (series.Version != expectedVersion)
        {
            throw new RecurringConflictException("This recurring series changed. Reload it before saving another decision.");
        }
    }

    public static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (from < MinimumDate || to > MaximumDate || to < from || to.DayNumber - from.DayNumber > 1827)
        {
            throw new ArgumentException("Choose dates between 1900-01-01 and 9998-12-31, in order, spanning at most 1827 days.");
        }
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentException("Use a positive page and page size 1–100.");
        }
    }

    private static void ValidateSettings(string name, string cadence, DateOnly anchorDate, decimal expectedAmount, string amountMode, string state)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 || !RecurringCalendar.IsSupported(cadence)
            || anchorDate < MinimumDate || anchorDate > MaximumDate || expectedAmount <= 0 || expectedAmount > 9999999999999999.99m
            || decimal.Round(expectedAmount, 2) != expectedAmount || amountMode is not "fixed" and not "variable"
            || state is not "active" and not "paused" and not "cancelled")
        {
            throw new ArgumentException("Provide a name up to 120 characters, supported cadence, valid anchor date, positive amount with at most two decimals, fixed/variable amount mode and active/paused/cancelled state.");
        }
    }

    private static void ValidateKind(string? kind)
    {
        if (kind is not null and not "subscription" and not "bill")
        {
            throw new ArgumentException("Choose subscription or bill.");
        }
    }

    private static void ValidateOccurrence(RecurringPaymentSeries series, DateOnly date)
    {
        if (date < series.AnchorDate || date < MinimumDate || date > MaximumDate || RecurringCalendar.Resolve(series.Cadence, series.AnchorDate, date).Date != date)
        {
            throw new ArgumentException("Choose a scheduled occurrence on or after the series anchor date.");
        }
    }

    private static decimal AnnualCost(RecurringPaymentSeries series) => series.ExpectedAmount * (series.Cadence switch
    {
        "weekly" => 52m, "fortnightly" => 26m, "monthly" => 12m, "quarterly" => 4m, _ => 1m
    });
    private static string SuggestedKind(string alias) => alias.StartsWith("direct debit", StringComparison.Ordinal) || alias.StartsWith("transfer", StringComparison.Ordinal) ? "bill" : "subscription";
    private DateOnly DefaultFrom() => Today.AddDays(-1096);
    private DateOnly DefaultTo() => Today.AddDays(366);
    private static DateTimeOffset Timestamp(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    private static string AccountName(Transaction row) => row.Account is null ? "Account unavailable" : AccountPreferences.DisplayName(row.Account);
    public static RecurringTransactionEvidence Evidence(Transaction row) => new(row.Id, row.AccountId, AccountName(row), row.Amount, row.Currency,
        row.PostedAt, row.MerchantName, row.Description, row.Reference, row.Status, Fingerprint(row));
    private static string Fingerprint(Transaction row) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        row.Id, row.AccountId, row.Amount, row.Currency, row.PostedAt, row.MerchantName, row.Description, row.Reference, row.Status
    }))));
}
