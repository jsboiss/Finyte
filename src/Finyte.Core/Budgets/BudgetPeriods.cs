namespace Finyte.Core.Budgets;

public sealed record BudgetPeriod(DateOnly From, DateOnly To)
{
    public DateOnly EndExclusive => To.AddDays(1);
}

public static class BudgetPeriods
{
    public static bool SupportedDate(DateOnly date) => date.Year is >= 1901 and <= 9990;

    // The anchor is a known boundary, not a creation date. Recurrences also extend backwards.
    public static BudgetPeriod Containing(string frequency, DateOnly anchor, DateOnly date)
    {
        if (frequency == "monthly")
        {
            var monthOffset = (date.Year - anchor.Year) * 12 + date.Month - anchor.Month;
            if (anchor.AddMonths(monthOffset) > date)
            {
                monthOffset--;
            }
            // Always add to the original anchor so January 31 -> February 28 -> March 31.
            return new BudgetPeriod(anchor.AddMonths(monthOffset), anchor.AddMonths(monthOffset + 1).AddDays(-1));
        }
        var length = frequency switch
        {
            "weekly" => 7,
            "fortnightly" => 14,
            _ => throw new ArgumentException("Unsupported budget frequency.", nameof(frequency))
        };
        var periodOffset = (int)Math.Floor((date.DayNumber - anchor.DayNumber) / (decimal)length);
        var from = anchor.AddDays(periodOffset * length);
        return new BudgetPeriod(from, from.AddDays(length - 1));
    }

    public static IReadOnlyList<BudgetPeriod> History(string frequency, DateOnly anchor, DateOnly date, int count)
    {
        var periods = new List<BudgetPeriod>();
        var current = Containing(frequency, anchor, date);
        for (var index = 0; index < count; index++)
        {
            periods.Add(current);
            current = Containing(frequency, anchor, current.From.AddDays(-1));
        }
        return periods;
    }
}
