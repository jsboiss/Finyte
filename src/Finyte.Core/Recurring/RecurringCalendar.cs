namespace Finyte.Core.Recurring;

public static class RecurringCalendar
{
    public static IReadOnlyList<string> SupportedCadences { get; } = ["weekly", "fortnightly", "monthly", "quarterly", "yearly"];
    public static DateOnly MinimumDate { get; } = new(1900, 1, 1);
    public static DateOnly MaximumDate { get; } = new(9998, 12, 31);

    public static bool IsSupported(string? cadence) => cadence is "weekly" or "fortnightly" or "monthly" or "quarterly" or "yearly";

    // Every occurrence is calculated from the original anchor, so February and late postings cannot shift the schedule.
    public static DateOnly? Add(string cadence, DateOnly anchor, int occurrenceIndex)
    {
        Validate(cadence, anchor);
        if (cadence is "weekly" or "fortnightly")
        {
            var dayNumber = anchor.DayNumber + (long)occurrenceIndex * (cadence == "weekly" ? 7 : 14);
            return dayNumber >= MinimumDate.DayNumber && dayNumber <= MaximumDate.DayNumber
                ? DateOnly.FromDayNumber((int)dayNumber) : null;
        }

        var months = cadence == "monthly" ? 1 : cadence == "quarterly" ? 3 : 12;
        var monthIndex = (anchor.Year - 1L) * 12 + anchor.Month - 1 + (long)occurrenceIndex * months;
        var minimumMonth = (MinimumDate.Year - 1L) * 12;
        var maximumMonth = (MaximumDate.Year - 1L) * 12 + 11;
        if (monthIndex < minimumMonth || monthIndex > maximumMonth)
        {
            return null;
        }

        var year = (int)(monthIndex / 12) + 1;
        var month = (int)(monthIndex % 12) + 1;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var anchorIsMonthEnd = anchor.Day >= 29 && anchor.Day == DateTime.DaysInMonth(anchor.Year, anchor.Month);
        return new DateOnly(year, month, anchorIsMonthEnd ? daysInMonth : Math.Min(anchor.Day, daysInMonth));
    }

    public static RecurringOccurrence Resolve(string cadence, DateOnly anchor, DateOnly date)
    {
        Validate(cadence, anchor);
        if (date < MinimumDate || date > MaximumDate)
        {
            throw new ArgumentOutOfRangeException(nameof(date), "Dates must be between 1900 and 9998.");
        }

        var approximateIndex = cadence switch
        {
            "weekly" => (date.DayNumber - anchor.DayNumber) / 7,
            "fortnightly" => (date.DayNumber - anchor.DayNumber) / 14,
            "monthly" => (date.Year - anchor.Year) * 12 + date.Month - anchor.Month,
            "quarterly" => ((date.Year - anchor.Year) * 12 + date.Month - anchor.Month) / 3,
            _ => date.Year - anchor.Year
        };
        var closestIndex = 0;
        var dueDate = anchor;
        var closestDistance = int.MaxValue;
        for (var index = approximateIndex - 2; index <= approximateIndex + 2; index++)
        {
            var candidate = Add(cadence, anchor, index);
            if (candidate is { } candidateDate && Math.Abs(candidateDate.DayNumber - date.DayNumber) < closestDistance)
            {
                closestIndex = index;
                dueDate = candidateDate;
                closestDistance = Math.Abs(candidateDate.DayNumber - date.DayNumber);
            }
        }
        return new RecurringOccurrence(dueDate, Add(cadence, anchor, closestIndex - 1), Add(cadence, anchor, closestIndex + 1),
            DateOnly.FromDayNumber(Math.Max(MinimumDate.DayNumber, dueDate.DayNumber - 3)),
            DateOnly.FromDayNumber(Math.Min(MaximumDate.DayNumber, dueDate.DayNumber + 3)), closestIndex);
    }

    private static void Validate(string cadence, DateOnly anchor)
    {
        if (!IsSupported(cadence))
        {
            throw new ArgumentException("Choose weekly, fortnightly, monthly, quarterly or yearly.", nameof(cadence));
        }
        if (anchor < MinimumDate || anchor > MaximumDate)
        {
            throw new ArgumentOutOfRangeException(nameof(anchor), "Dates must be between 1900 and 9998.");
        }
    }
}

public sealed record RecurringOccurrence(DateOnly Date, DateOnly? PreviousDate, DateOnly? NextDate, DateOnly WindowFrom, DateOnly WindowTo, int Index);
