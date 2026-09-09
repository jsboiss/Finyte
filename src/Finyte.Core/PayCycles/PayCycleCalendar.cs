namespace Finyte.Core.PayCycles;

public static class PayCycleCalendar
{
    // Leave room on either side for period boundaries and adjacent-cycle navigation.
    public static DateOnly MinimumDate { get; } = new(1900, 1, 1);
    public static DateOnly MaximumDate { get; } = new(9998, 12, 31);
    public static IReadOnlyList<string> Frequencies { get; } = ["weekly", "fortnightly", "monthly"];

    public static bool ValidDate(DateOnly date) => date >= MinimumDate && date <= MaximumDate;

    public static PayCyclePeriod Resolve(string frequency, DateOnly anchorDate, DateOnly date)
    {
        if (!Frequencies.Contains(frequency) || !ValidDate(anchorDate) || !ValidDate(date))
        {
            throw new ArgumentException("Use a supported frequency and dates between 1900-01-01 and 9998-12-31.");
        }
        var period = Finyte.Core.Scheduling.AnchoredPeriods.Resolve(frequency, anchorDate, date);
        return new PayCyclePeriod(period.From, period.ToExclusive);
    }
}

public sealed record PayCyclePeriod(DateOnly From, DateOnly ToExclusive);
