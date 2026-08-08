using Finyte.Api.Tenancy;
using Finyte.Core.Analytics;
using Finyte.Data;
using Finyte.Data.Billing;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class CashFlowEndpoints
{
    public static IEndpointRouteBuilder MapCashFlowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cash-flow", GetCashFlow)
            .RequireAuthorization()
            .WithName("GetCashFlow");

        return app;
    }

    private static async Task<IResult> GetCashFlow(
        DateOnly from,
        DateOnly to,
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);

        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to view cash flow.", statusCode: StatusCodes.Status402PaymentRequired);
        }

        if (to < from)
        {
            return Results.BadRequest("The end date must be on or after the start date.");
        }

        var dayCount = to.DayNumber - from.DayNumber + 1;
        if (dayCount > 366)
        {
            return Results.BadRequest("Cash flow ranges cannot exceed 366 days.");
        }

        var fromTimestamp = new DateTimeOffset(from.Year, from.Month, from.Day, 0, 0, 0, TimeSpan.Zero);
        var toTimestamp = new DateTimeOffset(to.Year, to.Month, to.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
        var currency = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId && (accountId == null || x.Id == accountId))
            .Select(x => x.Currency)
            .FirstOrDefaultAsync(cancellationToken) ?? "AUD";
        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId
                && (accountId == null || x.AccountId == accountId)
                && x.PostedAt >= fromTimestamp
                && x.PostedAt < toTimestamp
                && (x.Status == null || x.Status == "" || x.Status.ToLower() == "posted"))
            .Select(x => new { x.Amount, x.PostedAt })
            .ToListAsync(cancellationToken);
        var days = Enumerable.Range(0, dayCount)
            .Select(x => new CashFlowDay(from.AddDays(x)))
            .ToDictionary(x => x.Date);

        foreach (var transaction in transactions)
        {
            if (transaction.PostedAt is null)
            {
                continue;
            }

            var date = DateOnly.FromDateTime(transaction.PostedAt.Value.UtcDateTime);
            if (!days.TryGetValue(date, out var day))
            {
                continue;
            }

            var amount = ToMinorUnits(transaction.Amount);
            if (amount > 0)
            {
                day.IncomeMinorUnits += amount;
            }
            else
            {
                day.ExpenseMinorUnits += Math.Abs(amount);
            }
        }

        var response = new CashFlowRangeResponse(
            from.ToString("yyyy-MM-dd"),
            to.ToString("yyyy-MM-dd"),
            currency,
            days.Values
                .OrderBy(x => x.Date)
                .Select(x => new OverviewDailyCashFlowResponse(
                    x.Date.ToString("yyyy-MM-dd"),
                    x.Date.Day,
                    x.IncomeMinorUnits,
                    x.ExpenseMinorUnits))
                .ToList());

        return Results.Ok(response);
    }

    private static long ToMinorUnits(decimal amount)
    {
        return (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
    }

    private sealed class CashFlowDay(DateOnly date)
    {
        public DateOnly Date { get; } = date;

        public long IncomeMinorUnits { get; set; }

        public long ExpenseMinorUnits { get; set; }
    }
}
