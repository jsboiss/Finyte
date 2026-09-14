using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Finyte.Core.Recurring;

public static class RecurringPatternDetector
{
    public static string NormalizeAlias(string? value) => Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    public static bool IsGenericAlias(string? value)
    {
        var normalized = NormalizeAlias(value);
        return normalized is "apple" or "apple com bill" or "apple services" or "paypal" or "pay pal" or "stripe" or "square"
            or "amazon" or "amazon marketplace" or "google" or "google services" or "card purchase" or "direct debit" or "payment"
            || normalized.Length < 3 || normalized.All(x => char.IsDigit(x) || char.IsWhiteSpace(x));
    }

    public static IReadOnlyList<RecurringPatternCandidate> Detect(IReadOnlyList<RecurringPatternTransaction> transactions)
    {
        var rows = transactions.Where(x => x.Amount < 0 && x.PostedDate >= RecurringCalendar.MinimumDate
                && x.PostedDate <= RecurringCalendar.MaximumDate && !string.IsNullOrWhiteSpace(x.Currency))
            .DistinctBy(x => x.Id)
            .Select(x => new PatternRow(x, string.IsNullOrWhiteSpace(x.MerchantName) ? "description" : "merchant",
                NormalizeAlias(string.IsNullOrWhiteSpace(x.MerchantName) ? x.Description : x.MerchantName)))
            .Where(x => x.Alias.Length > 0)
            .GroupBy(x => new { x.Transaction.AccountId, Currency = x.Transaction.Currency.Trim().ToUpperInvariant(), x.Field, x.Alias });
        var results = new List<RecurringPatternCandidate>();
        foreach (var rowGroup in rows)
        {
            var group = rowGroup.OrderBy(x => x.Transaction.PostedDate).ThenBy(x => x.Transaction.Id).ToList();
            if (group.Count < 3)
            {
                continue;
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

            var ordered = possible.OrderByDescending(x => x.Rows.Count).ThenBy(x => x.TotalResidual)
                .ThenBy(x => x.Rows[0].Occurrence.Date).ThenBy(x => x.Cadence, StringComparer.Ordinal)
                .DistinctBy(x => $"{x.Cadence}|{string.Join(',', x.Rows.Select(y => y.Row.Transaction.Id))}").ToList();
            var decomposed = ordered.Where(x => BetterExplainedBySeparatePatterns(x, ordered))
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
                var minimum = fit.Rows.Min(x => -x.Row.Transaction.Amount);
                var maximum = fit.Rows.Max(x => -x.Row.Transaction.Amount);
                if (minimum != maximum)
                {
                    evidence.Add($"Recorded amounts vary from {minimum.ToString("0.##", CultureInfo.InvariantCulture)} to {maximum.ToString("0.##", CultureInfo.InvariantCulture)} {rowGroup.Key.Currency}. The estimate uses the latest payment.");
                }
                if (IsGenericAlias(rowGroup.Key.Alias))
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

                // Price is deliberately absent: a changed price does not create a new identity or reset dismissal.
                var keyText = $"{rowGroup.Key.AccountId:N}|{rowGroup.Key.Currency}|{rowGroup.Key.Field}|{rowGroup.Key.Alias}|{fit.Cadence}|{Phase(fit.Cadence, fit.Anchor)}";
                var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(keyText)));
                results.Add(new RecurringPatternCandidate(key, string.IsNullOrWhiteSpace(latest.MerchantName) ? latest.Description!.Trim() : latest.MerchantName.Trim(),
                    rowGroup.Key.AccountId, rowGroup.Key.Currency, fit.Cadence, trackingAnchor, -latest.Amount,
                    rowGroup.Key.Field, rowGroup.Key.Alias, fit.Rows.Select(x => x.Row.Transaction.Id).ToList(), evidence));
                used.UnionWith(fit.Rows.Select(x => x.Row.Transaction.Id));
            }
        }

        return results.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AccountId).ThenBy(x => x.AnchorDate).ToList();
    }

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

    private sealed record PatternRow(RecurringPatternTransaction Transaction, string Field, string Alias);
    private sealed record SlotRow(PatternRow Row, RecurringOccurrence Occurrence);
    private sealed record PatternFit(string Cadence, DateOnly Anchor, List<SlotRow> Rows, int TotalResidual);
}

public sealed record RecurringPatternTransaction(Guid Id, Guid AccountId, string Currency, decimal Amount, DateOnly PostedDate,
    string? MerchantName, string? Description, string? Reference);

public sealed record RecurringPatternCandidate(string Key, string Name, Guid AccountId, string Currency, string Cadence,
    DateOnly AnchorDate, decimal ExpectedAmount, string AliasField, string AliasValue, IReadOnlyList<Guid> TransactionIds, IReadOnlyList<string> Evidence);
