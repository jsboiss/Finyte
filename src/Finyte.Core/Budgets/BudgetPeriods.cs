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
        var period = Finyte.Core.Scheduling.AnchoredPeriods.Resolve(frequency, anchor, date);
        return new BudgetPeriod(period.From, period.ToExclusive.AddDays(-1));
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
