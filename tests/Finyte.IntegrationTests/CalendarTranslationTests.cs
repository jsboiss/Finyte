using Finyte.Core.Scheduling;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class CalendarTranslationTests
{
    [Fact]
    public void DailyGroupingTranslatesToAtTimeZoneWithoutLoadingRows()
    {
        using var dbContext = new FinyteDbContext(new DbContextOptionsBuilder<FinyteDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=unused")
            .Options);
        var timeZoneId = FinancialCalendar.DefaultTimeZoneId;
        var sql = dbContext.Transactions
            .GroupBy(x => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(x.PostedAt!.Value.UtcDateTime, timeZoneId).Date)
            .Select(x => new { x.Key, Total = x.Sum(y => y.Amount) })
            .ToQueryString();

        Assert.Contains("AT TIME ZONE", sql);
        Assert.Contains("GROUP BY", sql);
    }

    [Fact]
    public void LocalMidnightBoundsRespectDaylightSaving()
    {
        var calendar = FinancialCalendar.For("Australia/Sydney", TimeProvider.System);

        Assert.Equal(new DateTimeOffset(2026, 6, 30, 14, 0, 0, TimeSpan.Zero), calendar.StartOf(new DateOnly(2026, 7, 1)));
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 13, 0, 0, TimeSpan.Zero), calendar.StartOf(new DateOnly(2027, 1, 1)));
        Assert.Equal(new DateOnly(2026, 10, 1), calendar.ToDate(new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero)));
        Assert.Equal(calendar.StartOf(new DateOnly(2026, 3, 2)), calendar.EndExclusive(new DateOnly(2026, 3, 1)));
        Assert.Equal(new DateOnly(2026, 8, 23), calendar.ToDate(calendar.NoonOf(new DateOnly(2026, 8, 23))));
    }
}
