namespace Finyte.Core.Recurring;

public sealed record RecurringMatchRanking(string Version, int Score, string Confidence, string MatchKind,
    IReadOnlyList<string> Reasons, IReadOnlyList<Guid> CompetingSeriesIds, int CompetingPaymentCount);

// Scores order review suggestions. They are not calibrated probabilities or permission to confirm.
public static class RecurringCandidateRanker
{
    public static string Version => "recurring-candidates-v1";
    private static HashSet<string> GenericWords { get; } = new(StringComparer.Ordinal)
    {
        "apple", "paypal", "pay", "pal", "stripe", "square", "amazon", "google", "com", "bill", "billing",
        "payment", "payments", "purchase", "card", "direct", "debit", "subscription", "services", "service",
        "online", "pty", "ltd", "limited", "au", "aus", "australia", "new", "the"
    };

    public static IReadOnlyDictionary<Guid, RecurringMatchRanking> Rank(RecurringPaymentSeries target,
        IReadOnlyList<RecurringPaymentSeries> peers, IReadOnlyList<RecurringPatternTransaction> transactions,
        IReadOnlyList<RecurringPaymentDecision> decisions, DateOnly? occurrenceDate = null)
    {
        var reservations = decisions.Where(x => x.Status == "confirmed").ToLookup(x => x.TransactionId);
        var occupied = decisions.Where(x => x.Status == "confirmed").ToLookup(x => (x.SeriesId, x.OccurrenceDate));
        var rejected = decisions.Where(x => x.Status == "rejected").Select(x => (x.SeriesId, x.TransactionId, x.OccurrenceDate)).ToHashSet();
        var profiles = peers.Append(target).DistinctBy(x => x.Id)
            .Where(x => x.TenantId == target.TenantId && x.AccountId == target.AccountId && x.Currency == target.Currency)
            .ToDictionary(x => x.Id, x => new Profile(x, x.Aliases.Select(y => new Alias(y.Field, y.NormalizedValue, Tokens(y.NormalizedValue))).ToList()));
        var profile = profiles[target.Id];

        Assessment Assess(Profile current, RecurringPatternTransaction row, DateOnly? date = null)
        {
            var occurrence = RecurringCalendar.Resolve(current.Series.Cadence, current.Series.AnchorDate, date ?? row.PostedDate);
            var blocked = current.Series.State != "active" || occurrence.Date < current.Series.AnchorDate
                || row.AccountId != current.Series.AccountId || row.Currency != current.Series.Currency
                || reservations[row.Id].Any() || occupied[(current.Series.Id, occurrence.Date)].Any()
                || rejected.Contains((current.Series.Id, row.Id, occurrence.Date));
            return Evaluate(current, row, occurrence, blocked);
        }

        var assessments = transactions.ToDictionary(x => x.Id, x => Assess(profile, x, occurrenceDate));
        var windows = assessments.Where(x => x.Value.Score >= 55).ToLookup(x => x.Value.Date);
        var result = new Dictionary<Guid, RecurringMatchRanking>();
        foreach (var transaction in transactions)
        {
            var assessment = assessments[transaction.Id];
            var competingSeries = new List<Guid>();
            var competingPayments = 0;
            if (assessment.Score >= 55)
            {
                competingPayments = windows[assessment.Date].Count(x => x.Key != transaction.Id && x.Value.Score >= assessment.Score - 15);
                foreach (var otherProfile in profiles.Values.Where(x => x.Series.Id != target.Id && x.Series.State == "active"))
                {
                    var other = Assess(otherProfile, transaction);
                    if (other.Score >= 55 && other.Score >= assessment.Score - 15)
                    {
                        competingSeries.Add(otherProfile.Series.Id);
                    }
                }
            }
            var reasons = assessment.Reasons.ToList();
            var ambiguous = competingPayments > 0 || competingSeries.Count > 0;
            if (competingPayments > 0)
            {
                reasons.Add($"{competingPayments} other plausible payment(s) share this occurrence window, including outside the displayed date range.");
            }
            if (competingSeries.Count > 0)
            {
                var names = competingSeries.OrderBy(x => x).Take(3).Select(x => profiles[x].Series.Name);
                reasons.Add($"This payment also plausibly matches {competingSeries.Count} other active recurring series: {string.Join(", ", names)}. Review the alternatives before assigning it.");
            }
            result[transaction.Id] = new RecurringMatchRanking(Version, assessment.Score,
                ambiguous ? "ambiguous" : assessment.Score >= 80 ? "high" : assessment.Score >= 55 ? "medium" : "low",
                assessment.Kind, reasons, competingSeries.OrderBy(x => x).ToList(), competingPayments);
        }
        return result;
    }

    private static Assessment Evaluate(Profile profile, RecurringPatternTransaction row, RecurringOccurrence occurrence, bool blocked)
    {
        var merchant = RecurringPatternDetector.NormalizeAlias(row.MerchantName);
        var description = RecurringPatternDetector.NormalizeAlias(row.Description);
        var exact = profile.Aliases.Where(x => x.Value == (x.Field == "merchant" ? merchant : description)).ToList();
        var strongName = exact.Any(x => x.Tokens.Count > 0 && !RecurringPatternDetector.IsGenericAlias(x.Value));
        var relatedName = !strongName && profile.Aliases.Any(x => SharesDistinctiveName(x.Tokens, Tokens(x.Field == "merchant" ? merchant : description)));
        var nearDate = row.PostedDate >= occurrence.WindowFrom && row.PostedDate <= occurrence.WindowTo;
        var difference = Math.Abs(-row.Amount - profile.Series.ExpectedAmount);
        var score = strongName ? 60 : relatedName ? 35 : exact.Count > 0 ? 10 : 0;
        var kind = strongName ? "approved-name" : relatedName ? "possible-name-change" : exact.Count > 0 ? "shared-billing-name" : "weak-evidence";
        var reasons = new List<string>
        {
            strongName ? "Matches a distinctive billing name explicitly approved for this series."
                : relatedName ? "Shares distinctive words with an approved billing name; this may be a vendor name change."
                : exact.Count > 0 ? "Only a shared processor or generic billing name matches; it cannot identify the subscription."
                : "No distinctive approved billing name matches. Timing and amount alone are weak evidence.",
            nearDate ? $"Posted {Math.Abs(row.PostedDate.DayNumber - occurrence.Date.DayNumber)} day(s) from the scheduled date."
                : "Outside the three-day payment window; only a manual review can establish the relationship."
        };
        score += nearDate ? 25 : 0;
        if (difference == 0)
        {
            score += 10;
            reasons.Add("Amount equals the current estimate; this is supporting evidence only.");
        }
        else
        {
            // A fixed-price change may still have a strong identity match. Never rewrite the estimate.
            score += profile.Series.AmountMode == "fixed" && difference <= profile.Series.ExpectedAmount * 0.25m ? 5 : 0;
            reasons.Add(profile.Series.AmountMode == "variable"
                ? "Amount differs from the variable estimate; price is not used to establish identity."
                : "Amount differs from the saved price. Confirming this payment will not update the expected price.");
        }
        if (!nearDate)
        {
            score = Math.Min(score, 40);
        }
        if (blocked)
        {
            score = 0;
            kind = "review-only";
            reasons.Add("Not an open matching suggestion: a prior decision, reserved occurrence, inactive series or scope boundary requires manual review.");
        }
        return new Assessment(occurrence.Date, score, kind, reasons);
    }

    private static HashSet<string> Tokens(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(x => x.Length >= 3 && x.Any(y => char.IsLetter(y)) && !GenericWords.Contains(x)).ToHashSet(StringComparer.Ordinal);

    private static bool SharesDistinctiveName(HashSet<string> approved, HashSet<string> observed)
    {
        var shared = approved.Intersect(observed).Count();
        return shared > 0 && (decimal)shared / Math.Max(approved.Count, observed.Count) >= 0.5m;
    }

    private sealed record Alias(string Field, string Value, HashSet<string> Tokens);
    private sealed record Profile(RecurringPaymentSeries Series, IReadOnlyList<Alias> Aliases);
    private sealed record Assessment(DateOnly Date, int Score, string Kind, IReadOnlyList<string> Reasons);
}
