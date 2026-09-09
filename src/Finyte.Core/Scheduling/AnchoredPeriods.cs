namespace Finyte.Core.Scheduling;

public sealed record AnchoredPeriod(DateOnly From, DateOnly ToExclusive);

public static class AnchoredPeriods
{
    // Callers validate their supported date range, leaving room for adjacent boundaries.
    public static AnchoredPeriod Resolve(string frequency, DateOnly anchor, DateOnly date)
    {
        if (frequency == "monthly")
        {
            var offset = (date.Year - anchor.Year) * 12 + date.Month - anchor.Month;
            if (anchor.AddMonths(offset) > date)
            {
                offset--;
            }
            // Always use the original day: January 31 -> February 28 -> March 31.
            return new AnchoredPeriod(anchor.AddMonths(offset), anchor.AddMonths(offset + 1));
        }
        var length = frequency switch
        {
            "weekly" => 7,
            "fortnightly" => 14,
            _ => throw new ArgumentException("Unsupported schedule frequency.", nameof(frequency))
        };
        var periodNumber = (int)Math.Floor((date.DayNumber - anchor.DayNumber) / (decimal)length);
        var from = anchor.AddDays(periodNumber * length);
        return new AnchoredPeriod(from, from.AddDays(length));
    }
}
