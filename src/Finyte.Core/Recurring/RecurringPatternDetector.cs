using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Finyte.Core.Recurring;

public static class RecurringPatternDetector
{
    private static readonly string[] BankFeePrefixes = ["international transaction fee"];

    public static string NormalizeAlias(string? value) => Regex.Replace(StatementNameCleaner.Clean(value).Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    public static bool IsGenericAlias(string? value)
    {
        var normalized = NormalizeAlias(value);
        return normalized is "apple" or "apple com bill" or "apple services" or "paypal" or "pay pal" or "stripe" or "square"
            or "amazon" or "amazon marketplace" or "google" or "google services" or "card purchase" or "direct debit" or "payment"
            || normalized.Length < 3 || normalized.All(x => char.IsDigit(x) || char.IsWhiteSpace(x));
    }

    public static bool IsBankFee(string alias) => BankFeePrefixes.Any(x => alias.StartsWith(x, StringComparison.Ordinal));

    public static IReadOnlyList<RecurringPatternCandidate> Detect(IReadOnlyList<RecurringPatternTransaction> transactions, RecurringDetectionOptions? options = null)
    {
        options ??= new RecurringDetectionOptions();
        var rows = transactions.Where(x => x.Amount < 0 && x.PostedDate >= RecurringCalendar.MinimumDate
                && x.PostedDate <= RecurringCalendar.MaximumDate && !string.IsNullOrWhiteSpace(x.Currency))
            .DistinctBy(x => x.Id)
            .Select(x => new PatternRow(x, string.IsNullOrWhiteSpace(x.MerchantName) ? "description" : "merchant",
                NormalizeAlias(string.IsNullOrWhiteSpace(x.MerchantName) ? x.Description : x.MerchantName)))
            .Where(x => x.Alias.Length > 0 && !IsBankFee(x.Alias))
            .ToList();
        if (rows.Count == 0)
        {
            return [];
        }
        var latestByAccount = rows.GroupBy(x => x.Transaction.AccountId).ToDictionary(x => x.Key, x => x.Max(y => y.Transaction.PostedDate));
        var results = new List<RecurringPatternCandidate>();
        foreach (var rowGroup in rows.GroupBy(x => new GroupKey(x.Transaction.AccountId, x.Transaction.Currency.Trim().ToUpperInvariant(), x.Field, x.Alias)))
        {
            var group = rowGroup.OrderBy(x => x.Transaction.PostedDate).ThenBy(x => x.Transaction.Id).ToList();
            if (group.Count < (options.IncludeEarly ? 2 : 3))
            {
                continue;
            }
            var latest = latestByAccount[rowGroup.Key.AccountId];
            var asOf = options.AsOf is { } limit && limit < latest ? limit : latest;
            var found = new List<RecurringPatternCandidate>();
            var explained = 0;
            foreach (var part in Partition(group))
            {
                var detected = DetectPart(rowGroup.Key, part, asOf);
                found.AddRange(detected);
                explained += detected.Sum(x => x.TransactionIds.Count);
                if (options.IncludeEarly)
                {
                    var early = EarlyPairs(rowGroup.Key, part, found, asOf);
                    found.AddRange(early.Candidates);
                    explained += early.Explained;
                }
            }
            if (explained * 4 < group.Count * 3)
            {
                continue;
            }
            results.AddRange(found);
        }

        return results.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AccountId).ThenBy(x => x.AnchorDate).ToList();
    }

    private static IEnumerable<Part> Partition(List<PatternRow> group)
    {
        var busiest = 1;
        for (int start = 0, end = 0; end < group.Count; end++)
        {
            while (group[end].Transaction.PostedDate.DayNumber - group[start].Transaction.PostedDate.DayNumber > 3)
            {
                start++;
            }
            busiest = Math.Max(busiest, end - start + 1);
        }
        if (busiest != 2)
        {
            return [new Part(group, null, null, null)];
        }
        var byAmount = group.OrderBy(x => -x.Transaction.Amount).ThenBy(x => x.Transaction.Id).ToList();
        var cut = Enumerable.Range(1, byAmount.Count - 1).MaxBy(x => byAmount[x].Transaction.Amount / byAmount[x - 1].Transaction.Amount);
        if (byAmount[cut].Transaction.Amount / byAmount[cut - 1].Transaction.Amount < 1.25m)
        {
            return [new Part(group, null, null, null)];
        }
        var low = byAmount.Take(cut).Select(x => x.Transaction.Id).ToHashSet();
        var shared = group.Where((x, index) =>
            (index > 0 && x.Transaction.PostedDate.DayNumber - group[index - 1].Transaction.PostedDate.DayNumber <= 3)
            || (index < group.Count - 1 && group[index + 1].Transaction.PostedDate.DayNumber - x.Transaction.PostedDate.DayNumber <= 3)).ToList();
        var lastShared = shared.Max(x => x.Transaction.PostedDate);
        var singleWindow = lastShared.DayNumber - shared.Min(x => x.Transaction.PostedDate).DayNumber <= 3;
        bool Continues(bool isLow) => group.Any(x => low.Contains(x.Transaction.Id) == isLow && x.Transaction.PostedDate.DayNumber > lastShared.DayNumber + 3);
        if (!singleWindow || Continues(true) == Continues(false))
        {
            return [new Part(group.Where(x => low.Contains(x.Transaction.Id)).ToList(), "low", null, null),
                new Part(group.Where(x => !low.Contains(x.Transaction.Id)).ToList(), "high", null, null)];
        }
        var endingIsLow = !Continues(true);
        var dropped = shared.Where(x => low.Contains(x.Transaction.Id) == endingIsLow).ToList();
        var continuingStart = group.Where(x => low.Contains(x.Transaction.Id) != endingIsLow).Min(x => x.Transaction.PostedDate);
        var rows = group.Where(x => !dropped.Contains(x)).ToList();
        return continuingStart.DayNumber >= lastShared.DayNumber - 3
            ? [new Part(rows, null, lastShared, null)]
            : [new Part(rows, null, null, dropped[0].Transaction)];
    }

    public static bool IsBillAlias(string alias) =>
        alias.StartsWith("direct debit", StringComparison.Ordinal) || alias.StartsWith("transfer", StringComparison.Ordinal);

    private static bool StablePrice(IEnumerable<RecurringPatternTransaction> rows)
    {
        var amounts = rows.Select(x => -x.Amount).ToList();
        return amounts.Count == 0 || amounts.Max() <= amounts.Min() * 1.25m;
    }

    private static List<RecurringPatternCandidate> DetectPart(GroupKey key, Part part, DateOnly asOf)
    {
        var group = part.Rows;
        var results = new List<RecurringPatternCandidate>();
        if (group.Count < 3)
        {
            return results;
        }

        var possible = new List<PatternFit>();
        foreach (var cadence in RecurringCalendar.SupportedCadences)
        {
            // Distinct calendar phases bound the search even for a merchant with a long history.
            var anchors = group.Select(x => x.Transaction.PostedDate).DistinctBy(x => Phase(cadence, x));
            foreach (var anchor in anchors)
            {
                var slots = group.Select(x => new SlotRow(x, RecurringCalendar.Resolve(cadence, anchor, x.Transaction.PostedDate)))
                    .Where(x => x.Row.Transaction.PostedDate >= x.Occurrence.WindowFrom && x.Row.Transaction.PostedDate <= x.Occurrence.WindowTo)
                    .GroupBy(x => x.Occurrence.Index)
                    .OrderBy(x => x.Key);
                var run = new List<SlotRow>();
                foreach (var slot in slots)
                {
                    // Competing charges in the same billing window need manual assignment, never arbitrary selection.
                    if (slot.Count() != 1 || (run.Count > 0 && slot.Key != run[^1].Occurrence.Index + 1))
                    {
                        AddFit(possible, cadence, anchor, run);
                        run = [];
                    }
                    if (slot.Count() == 1)
                    {
                        run.Add(slot.Single());
                    }
                }
                AddFit(possible, cadence, anchor, run);
            }
        }
        var variableBill = IsBillAlias(key.Alias);
        possible.RemoveAll(x => (x.Cadence is "weekly" or "fortnightly" || !variableBill)
            && !StablePrice(x.Rows.Select(y => y.Row.Transaction).Where(y => part.PlanChange is not { } changed || y.PostedDate >= changed)));

        var ordered = possible.OrderByDescending(x => x.Rows.Count).ThenBy(x => x.TotalResidual)
            .ThenBy(x => x.Rows[0].Occurrence.Date).ThenBy(x => x.Cadence, StringComparer.Ordinal)
            .DistinctBy(x => $"{x.Cadence}|{string.Join(',', x.Rows.Select(y => y.Row.Transaction.Id))}").ToList();
        var decomposed = ordered.Where(x => !SpansPlanChange(x, part.PlanChange) && BetterExplainedBySeparatePatterns(x, ordered))
            .Select(x => new { x.Cadence, Ids = x.Rows.Select(y => y.Row.Transaction.Id).ToHashSet() }).ToList();
        var used = new HashSet<Guid>();
        foreach (var fit in ordered)
        {
            if (fit.Rows.Any(x => used.Contains(x.Row.Transaction.Id)))
            {
                continue;
            }
            if (decomposed.Any(x => x.Cadence == fit.Cadence && fit.Rows.All(y => x.Ids.Contains(y.Row.Transaction.Id))))
            {
                continue;
            }
            if (TrackingAnchor(fit) is not { } trackingAnchor)
            {
                continue;
            }

            var latest = fit.Rows[^1].Row.Transaction;
            var evidence = new List<string>
            {
                $"{fit.Rows.Count} consecutive {fit.Cadence} billing windows each contain one payment.",
                $"Every payment is within {fit.Rows.Max(x => Math.Abs(x.Row.Transaction.PostedDate.DayNumber - x.Occurrence.Date.DayNumber))} days of the anchored schedule.",
                "Payments share an account, currency and statement name. Review the individual payments before tracking."
            };
            AddAmountEvidence(evidence, fit.Rows.Select(x => x.Row.Transaction).ToList(), key.Currency);
            if (part.PlanChange is { } changed)
            {
                evidence.Add($"Plan or price changed on {changed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}. The earlier price's final charge on that day is not included.");
            }
            if (part.Excluded is { } excluded)
            {
                evidence.Add($"A separate charge of {(-excluded.Amount).ToString("0.##", CultureInfo.InvariantCulture)} {key.Currency} on {excluded.PostedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is not part of this pattern.");
            }
            if (IsGenericAlias(key.Alias))
            {
                evidence.Add("This is a shared payment processor or generic statement name; these payments may belong to different services.");
            }
            var alternatives = possible.Where(x => x.Cadence != fit.Cadence && x.Rows.Count == fit.Rows.Count && x.TotalResidual == fit.TotalResidual
                    && x.Rows.Select(y => y.Row.Transaction.Id).ToHashSet().SetEquals(fit.Rows.Select(y => y.Row.Transaction.Id)))
                .Select(x => x.Cadence).Distinct().ToList();
            if (alternatives.Count > 0)
            {
                evidence.Add($"The same evidence also fits {string.Join(" or ", alternatives)}. Choose the billing schedule when tracking.");
            }

            var ended = asOf.DayNumber - latest.PostedDate.DayNumber > PeriodDays(fit.Cadence) * 3 / 2;
            results.Add(new RecurringPatternCandidate(Key(key, fit.Cadence, fit.Anchor, part.Cluster), DisplayName(latest),
                key.AccountId, key.Currency, fit.Cadence, trackingAnchor, -latest.Amount,
                key.Field, key.Alias, fit.Rows.Select(x => x.Row.Transaction.Id).ToList(), evidence, IsEnded: ended));
            used.UnionWith(fit.Rows.Select(x => x.Row.Transaction.Id));
        }
        return results;
    }

    private static (List<RecurringPatternCandidate> Candidates, int Explained) EarlyPairs(GroupKey key, Part part, IReadOnlyCollection<RecurringPatternCandidate> found, DateOnly asOf)
    {
        if (IsGenericAlias(key.Alias))
        {
            return ([], 0);
        }
        var used = found.SelectMany(x => x.TransactionIds).ToHashSet();
        var rest = part.Rows.Where(x => !used.Contains(x.Transaction.Id)).ToList();
        if (rest.Count is < 2 or > 24)
        {
            return ([], 0);
        }
        var paired = new HashSet<Guid>();
        var pairs = new List<(RecurringPatternTransaction First, RecurringPatternTransaction Second)>();
        foreach (var first in rest.Select(x => x.Transaction))
        {
            if (paired.Contains(first.Id))
            {
                continue;
            }
            var due = first.PostedDate.AddMonths(1);
            var matches = rest.Select(x => x.Transaction).Where(x => x.Id != first.Id && !paired.Contains(x.Id)
                && Math.Abs(x.PostedDate.DayNumber - due.DayNumber) <= 3
                && Math.Abs(x.Amount - first.Amount) <= Math.Abs(x.Amount) * 0.05m).ToList();
            if (matches.Count != 1)
            {
                continue;
            }
            paired.Add(first.Id);
            paired.Add(matches[0].Id);
            pairs.Add((first, matches[0]));
        }
        if (pairs.Count == 0 || rest.Count - paired.Count > 1)
        {
            return ([], 0);
        }
        return (pairs.Where(x => asOf.DayNumber - x.Second.PostedDate.DayNumber <= 45).Select(x =>
        {
            var evidence = new List<string> { "Only two monthly payments so far, one month apart. Check both before tracking; a third payment will confirm the schedule." };
            AddAmountEvidence(evidence, [x.First, x.Second], key.Currency);
            return new RecurringPatternCandidate(Key(key, "monthly", x.First.PostedDate, part.Cluster), DisplayName(x.Second), key.AccountId, key.Currency,
                "monthly", x.First.PostedDate, -x.Second.Amount, key.Field, key.Alias, [x.First.Id, x.Second.Id], evidence, IsEarly: true);
        }).ToList(), paired.Count);
    }

    private static void AddAmountEvidence(List<string> evidence, IReadOnlyList<RecurringPatternTransaction> rows, string currency)
    {
        var minimum = rows.Min(x => -x.Amount);
        var maximum = rows.Max(x => -x.Amount);
        if (minimum != maximum)
        {
            evidence.Add($"Recorded amounts vary from {minimum.ToString("0.##", CultureInfo.InvariantCulture)} to {maximum.ToString("0.##", CultureInfo.InvariantCulture)} {currency}. The estimate uses the latest payment.");
        }
    }

    // Price is deliberately absent: a changed price does not create a new identity or reset dismissal.
    private static string Key(GroupKey key, string cadence, DateOnly anchor, string? cluster)
    {
        var keyText = $"{key.AccountId:N}|{key.Currency}|{key.Field}|{key.Alias}|{cadence}|{Phase(cadence, anchor)}" + (cluster is null ? "" : $"|price:{cluster}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(keyText)));
    }

    private static string DisplayName(RecurringPatternTransaction row) =>
        StatementNameCleaner.Clean(string.IsNullOrWhiteSpace(row.MerchantName) ? row.Description : row.MerchantName);

    private static int PeriodDays(string cadence) => cadence switch
    {
        "weekly" => 7, "fortnightly" => 14, "monthly" => 30, "quarterly" => 91, _ => 365
    };

    private static DateOnly? TrackingAnchor(PatternFit fit)
    {
        var phase = Phase(fit.Cadence, fit.Anchor);
        // A clipped February occurrence cannot be used as a new day-30 anchor. Walk back to the same original phase.
        for (var index = fit.Rows[0].Occurrence.Index; ; index--)
        {
            var candidate = RecurringCalendar.Add(fit.Cadence, fit.Anchor, index);
            if (candidate is null)
            {
                return null;
            }
            if (Phase(fit.Cadence, candidate.Value) == phase)
            {
                return candidate.Value;
            }
        }
    }

    private static bool SpansPlanChange(PatternFit fit, DateOnly? changed) =>
        changed is { } date && fit.Rows[0].Row.Transaction.PostedDate < date && fit.Rows[^1].Row.Transaction.PostedDate >= date;

    private static bool BetterExplainedBySeparatePatterns(PatternFit fit, List<PatternFit> alternatives)
    {
        if (fit.Rows.Count < 6 || fit.TotalResidual == 0)
        {
            return false;
        }
        var ids = fit.Rows.Select(x => x.Row.Transaction.Id).ToHashSet();
        var covered = new HashSet<Guid>();
        var residual = 0;
        var parts = alternatives.Where(x => x.Cadence != fit.Cadence && x.Rows.Count < fit.Rows.Count
                && x.Rows.All(y => ids.Contains(y.Row.Transaction.Id)))
            .OrderBy(x => (decimal)x.TotalResidual / x.Rows.Count).ThenByDescending(x => x.Rows.Count);
        foreach (var part in parts)
        {
            if (part.Rows.Any(x => covered.Contains(x.Row.Transaction.Id)))
            {
                continue;
            }
            covered.UnionWith(part.Rows.Select(x => x.Row.Transaction.Id));
            residual += part.TotalResidual;
        }
        // For example, bills on the 5th and 20th can resemble a drifting fortnightly charge. Keep the two exact monthly tracks.
        return covered.Count == ids.Count && residual < fit.TotalResidual;
    }

    private static void AddFit(List<PatternFit> fits, string cadence, DateOnly anchor, List<SlotRow> rows)
    {
        if (rows.Count >= 3)
        {
            fits.Add(new PatternFit(cadence, anchor, rows, rows.Sum(x => Math.Abs(x.Row.Transaction.PostedDate.DayNumber - x.Occurrence.Date.DayNumber))));
        }
    }

    private static string Phase(string cadence, DateOnly date)
    {
        var day = date.Day >= 29 && date.Day == DateTime.DaysInMonth(date.Year, date.Month) ? "end" : date.Day.ToString(CultureInfo.InvariantCulture);
        return cadence switch
        {
            "weekly" => (date.DayNumber % 7).ToString(CultureInfo.InvariantCulture),
            "fortnightly" => (date.DayNumber % 14).ToString(CultureInfo.InvariantCulture),
            "monthly" => day,
            "quarterly" => $"{date.Month % 3}|{day}",
            _ => $"{date.Month}|{day}"
        };
    }

    private sealed record GroupKey(Guid AccountId, string Currency, string Field, string Alias);
    private sealed record Part(List<PatternRow> Rows, string? Cluster, DateOnly? PlanChange, RecurringPatternTransaction? Excluded);
    private sealed record PatternRow(RecurringPatternTransaction Transaction, string Field, string Alias);
    private sealed record SlotRow(PatternRow Row, RecurringOccurrence Occurrence);
    private sealed record PatternFit(string Cadence, DateOnly Anchor, List<SlotRow> Rows, int TotalResidual);
}

public sealed record RecurringDetectionOptions(bool IncludeEarly = false, DateOnly? AsOf = null);

public sealed record RecurringPatternTransaction(Guid Id, Guid AccountId, string Currency, decimal Amount, DateOnly PostedDate,
    string? MerchantName, string? Description, string? Reference);

public sealed record RecurringPatternCandidate(string Key, string Name, Guid AccountId, string Currency, string Cadence,
    DateOnly AnchorDate, decimal ExpectedAmount, string AliasField, string AliasValue, IReadOnlyList<Guid> TransactionIds, IReadOnlyList<string> Evidence,
    bool IsEarly = false, bool IsEnded = false);
