using Finyte.Core.Scheduling;
using Finyte.Data;
using Finyte.Data.Tenancy;

namespace Finyte.IntegrationTests;

internal static class TestCalendar
{
    public static FinancialCalendar Default { get; } = FinancialCalendar.For(FinancialCalendar.DefaultTimeZoneId, TimeProvider.System);

    public static TenantCalendars Tenants(FinyteDbContext dbContext) => new(dbContext, TimeProvider.System);
}
