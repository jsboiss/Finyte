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
        if (frequency == "monthly")
        {
            var month = new DateOnly(date.Year, date.Month, 1);
            var start = AtAnchorDay(month, anchorDate.Day);
            if (date < start)
            {
                month = month.AddMonths(-1);
                start = AtAnchorDay(month, anchorDate.Day);
            }
            return new PayCyclePeriod(start, AtAnchorDay(month.AddMonths(1), anchorDate.Day));
        }
        var length = frequency == "weekly" ? 7 : 14;
        var offset = date.DayNumber - anchorDate.DayNumber;
        var periodNumber = (int)Math.Floor((decimal)offset / length);
        var from = anchorDate.AddDays(periodNumber * length);
        return new PayCyclePeriod(from, from.AddDays(length));
    }

    private static DateOnly AtAnchorDay(DateOnly month, int day) => new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
}

public sealed record PayCyclePeriod(DateOnly From, DateOnly ToExclusive);
