using Finyte.Core.Recurring;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class RecurringCalendarTests
{
    [Fact]
    public void MonthlySchedulePreservesMonthEndAcrossFebruaryAndLeapYears()
    {
        var anchor = new DateOnly(2024, 1, 31);
        Assert.Equal(new DateOnly(2024, 2, 29), RecurringCalendar.Add("monthly", anchor, 1));
        Assert.Equal(new DateOnly(2024, 3, 31), RecurringCalendar.Add("monthly", anchor, 2));
        Assert.Equal(new DateOnly(2025, 2, 28), RecurringCalendar.Add("monthly", anchor, 13));
        Assert.Equal(new DateOnly(2025, 3, 31), RecurringCalendar.Add("monthly", anchor, 14));
    }

    [Fact]
    public void ClippedNonMonthEndDateReturnsToOriginalDay()
    {
        var anchor = new DateOnly(2026, 1, 30);
        Assert.Equal(new DateOnly(2026, 2, 28), RecurringCalendar.Add("monthly", anchor, 1));
        Assert.Equal(new DateOnly(2026, 3, 30), RecurringCalendar.Add("monthly", anchor, 2));
        Assert.Equal(new DateOnly(2026, 3, 30), RecurringCalendar.Resolve("monthly", anchor, new DateOnly(2026, 3, 31)).Date);
    }

    [Theory]
    [InlineData("weekly", 2026, 1, 12)]
    [InlineData("fortnightly", 2026, 1, 19)]
    [InlineData("monthly", 2026, 2, 5)]
    [InlineData("quarterly", 2026, 4, 5)]
    [InlineData("yearly", 2027, 1, 5)]
    public void SupportedCadencesAdvanceFromTheirOriginalAnchor(string cadence, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), RecurringCalendar.Add(cadence, new DateOnly(2026, 1, 5), 1));
    }

    [Fact]
    public void ResolutionSupportsHistoricalOccurrencesAndDoesNotMoveScheduleAfterLatePosting()
    {
        var anchor = new DateOnly(2026, 5, 5);
        var occurrence = RecurringCalendar.Resolve("monthly", anchor, new DateOnly(2026, 3, 8));
        Assert.Equal(-2, occurrence.Index);
        Assert.Equal(new DateOnly(2026, 3, 5), occurrence.Date);
        Assert.Equal(new DateOnly(2026, 2, 5), occurrence.PreviousDate);
        Assert.Equal(new DateOnly(2026, 4, 5), occurrence.NextDate);
        Assert.Equal(new DateOnly(2026, 3, 2), occurrence.WindowFrom);
        Assert.Equal(new DateOnly(2026, 3, 8), occurrence.WindowTo);
    }

    [Fact]
    public void EquidistantOccurrenceUsesEarlierDate()
    {
        var occurrence = RecurringCalendar.Resolve("fortnightly", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8));
        Assert.Equal(new DateOnly(2026, 1, 1), occurrence.Date);
    }

    [Fact]
    public void BoundsDoNotOverflowAndWindowsAreClamped()
    {
        Assert.Null(RecurringCalendar.Add("weekly", RecurringCalendar.MinimumDate, int.MinValue));
        Assert.Null(RecurringCalendar.Add("yearly", RecurringCalendar.MaximumDate, int.MaxValue));
        var first = RecurringCalendar.Resolve("monthly", RecurringCalendar.MinimumDate, RecurringCalendar.MinimumDate);
        Assert.Null(first.PreviousDate);
        Assert.Equal(RecurringCalendar.MinimumDate, first.WindowFrom);
        var last = RecurringCalendar.Resolve("yearly", RecurringCalendar.MaximumDate, RecurringCalendar.MaximumDate);
        Assert.Null(last.NextDate);
        Assert.Equal(RecurringCalendar.MaximumDate, last.WindowTo);
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurringCalendar.Resolve("weekly", new DateOnly(1899, 12, 31), RecurringCalendar.MinimumDate));
        Assert.Throws<ArgumentException>(() => RecurringCalendar.Resolve("daily", RecurringCalendar.MinimumDate, RecurringCalendar.MinimumDate));
    }
}
