using Finyte.Api.Tenancy;
using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Data;
using Finyte.Data.Billing;
using Finyte.Data.Tenancy;
using Finyte.Data.Transfers;
using Microsoft.AspNetCore.Mvc;
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
        [FromQuery] Guid[]? accountIds,
        Guid? groupId,
        bool? includeInternalTransfers,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        FinyteDbContext dbContext,
        TenantCalendars calendars,
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

        var resolved = await OverviewEndpoints.ResolveScope(dbContext, currentTenant.TenantId, accountId, accountIds, groupId, cancellationToken);
        if (resolved.Error is { } error)
        {
            return error;
        }
        var scope = resolved.Scope!;
        var explicitScope = scope.AccountId != null || scope.AccountIds != null;
        var calendar = await calendars.For(currentTenant.TenantId, cancellationToken);
        var fromTimestamp = calendar.StartOf(from);
        var toTimestamp = calendar.EndExclusive(to);
        var timeZoneId = calendar.TimeZoneId;
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId && (scope.AccountId == null || x.Id == scope.AccountId)
                && (scope.AccountIds == null || scope.AccountIds.Contains(x.Id)))
            .OrderBy(x => x.CustomName ?? x.Name).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var currency = AccountPreferences.AnalyticsCurrency(accounts, explicitScope ? accounts.FirstOrDefault()?.Id : null);
        var scopeAccountIds = accounts.Where(x => explicitScope || AccountPreferences.IncludeInAnalytics(x)).Select(x => x.Id).ToList();
        var transactionQuery = dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId
                && scopeAccountIds.Contains(x.AccountId)
                && x.Currency == currency
                && x.PostedAt >= fromTimestamp
                && x.PostedAt < toTimestamp
                && (x.Status == null || x.Status == "" || x.Status.ToLower() == "posted"));
        if (includeInternalTransfers != true)
        {
            transactionQuery = transactionQuery.ExcludeInternalTransfers();
        }
        var dailyTotals = await transactionQuery
            .GroupBy(x => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(x.PostedAt!.Value.UtcDateTime, timeZoneId).Date)
            .Select(x => new
            {
                Date = x.Key,
                Income = x.Sum(y => y.Amount > 0 ? y.Amount : 0),
                Expense = x.Sum(y => y.Amount < 0 ? -y.Amount : 0)
            })
            .ToListAsync(cancellationToken);
        var days = Enumerable.Range(0, dayCount)
            .Select(x => new CashFlowDay(from.AddDays(x)))
            .ToDictionary(x => x.Date);

        foreach (var dailyTotal in dailyTotals)
        {
            if (!days.TryGetValue(DateOnly.FromDateTime(dailyTotal.Date), out var day))
            {
                continue;
            }

            day.IncomeMinorUnits = ToMinorUnits(dailyTotal.Income);
            day.ExpenseMinorUnits = ToMinorUnits(dailyTotal.Expense);
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
