using Finyte.Core.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Tenancy;

public sealed class TenantCalendars(FinyteDbContext dbContext, TimeProvider timeProvider)
{
    private Dictionary<Guid, FinancialCalendar> Cache { get; } = [];

    public async Task<FinancialCalendar> For(Guid tenantId, CancellationToken cancellationToken)
    {
        if (Cache.TryGetValue(tenantId, out var cached))
        {
            return cached;
        }

        var timeZoneId = await dbContext.Tenants.AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => x.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);
        var calendar = FinancialCalendar.For(FinancialCalendar.IsValidTimeZoneId(timeZoneId) ? timeZoneId! : FinancialCalendar.DefaultTimeZoneId, timeProvider);
        Cache[tenantId] = calendar;
        return calendar;
    }
}
