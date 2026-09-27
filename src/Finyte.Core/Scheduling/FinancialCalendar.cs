namespace Finyte.Core.Scheduling;

// Instants are stored in UTC. Every financial date is that instant read in the household's time zone.
public sealed class FinancialCalendar(TimeZoneInfo timeZone, TimeProvider timeProvider)
{
    public const string DefaultTimeZoneId = "Australia/Sydney";

    public static bool IsValidTimeZoneId(string? timeZoneId)
    {
        return !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);
    }

    public static FinancialCalendar For(string timeZoneId, TimeProvider timeProvider)
    {
        return new FinancialCalendar(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId), timeProvider);
    }

    public string TimeZoneId => timeZone.Id;

    public DateOnly Today => ToDate(timeProvider.GetUtcNow());

    public string CurrentMonthKey => MonthKey(Today);

    public static string MonthKey(DateOnly date) => $"{date.Year:D4}-{date.Month:D2}";

    public DateOnly ToDate(DateTimeOffset instant)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
    }

    // Local midnight as a UTC instant, so range filters stay index-friendly comparisons on PostedAt.
    public DateTimeOffset StartOf(DateOnly date)
    {
        if (date >= DateOnly.MaxValue)
        {
            return DateTimeOffset.MaxValue;
        }

        if (date <= DateOnly.MinValue)
        {
            return DateTimeOffset.MinValue;
        }

        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    public DateTimeOffset EndExclusive(DateOnly date)
    {
        return date >= DateOnly.MaxValue ? DateTimeOffset.MaxValue : StartOf(date.AddDays(1));
    }

    // A date-only source (a statement) is stored at local noon so the day survives a later zone change.
    public DateTimeOffset NoonOf(DateOnly date)
    {
        return StartOf(date).AddHours(12);
    }
}
