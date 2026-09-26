using System.Data;
using Finyte.Api.Tenancy;
using Finyte.Core.Budgets;
using Finyte.Data;
using Finyte.Data.Billing;
using Finyte.Data.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/budgets").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var tenant = await services.GetRequiredService<TenantResolver>().Resolve(context.HttpContext.User, context.HttpContext.RequestAborted);
            if (!await services.GetRequiredService<IBillingAccess>().HasAccess(tenant.TenantId, context.HttpContext.RequestAborted))
            {
                return Results.Problem("An active subscription is required to use budgets.", statusCode: StatusCodes.Status402PaymentRequired);
            }
            return await next(context);
        });
        group.MapGet("/", GetBudgets).WithName("GetBudgets");
        group.MapGet("/categories", GetCategories).WithName("GetBudgetCategories");
        group.MapPost("/preview", Preview).WithName("PreviewBudget");
        group.MapPost("/", CreateBudget).WithName("CreateBudget");
        group.MapPut("/{budgetId:guid}", UpdateBudget).WithName("UpdateBudget");
        group.MapDelete("/{budgetId:guid}", DeleteBudget).WithName("DeleteBudget");
        group.MapGet("/{budgetId:guid}/periods", GetPeriods).WithName("GetBudgetPeriods");
        group.MapGet("/{budgetId:guid}/transactions", GetTransactions).WithName("GetBudgetTransactions");
        return app;
    }

    private static async Task<string[]> AvailableCategories(FinyteDbContext dbContext, Guid tenantId, CancellationToken cancellationToken)
    {
        var transactions = dbContext.Transactions.Where(x => x.TenantId == tenantId);
        var categories = await transactions.Select(x => x.PrimaryCategory)
            .Union(transactions.Select(x => x.SecondaryCategory))
            .Union(transactions.Select(x => x.CategoryOverride))
            .Union(transactions.Select(x => x.CategoryFromRule))
            .Where(x => x != null && x != "").ToListAsync(cancellationToken);
        return categories.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Where(x => x.Length <= 120)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<IResult> GetCategories(TenantResolver tenantResolver, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        return Results.Ok(await AvailableCategories(dbContext, tenant.TenantId, cancellationToken));
    }

    private static async Task<IResult> Preview(BudgetRequest request, DateOnly date, TenantResolver tenantResolver, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var error = Validate(request);
        if (error != null || !BudgetPeriods.SupportedDate(date))
        {
            return Results.BadRequest(error ?? "Choose a preview date from 1901 through 9990.");
        }
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var snapshot = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        error = await ValidateReferences(dbContext, tenant.TenantId, request, cancellationToken);
        if (error != null)
        {
            return Results.BadRequest(error);
        }
        var budget = new Budget { TenantId = tenant.TenantId, Name = request.Name! };
        Apply(budget, request);
        var period = BudgetPeriods.Containing(budget.Frequency, budget.AnchorDate, date);
        var query = (await BudgetQueries.Transactions(dbContext, budget, cancellationToken, includeOtherCurrencies: true)).InPeriod(period);
        var totals = await query.GroupBy(x => x.Currency)
            .Select(x => new { Currency = x.Key, Spent = x.Sum(y => -y.Amount), Count = x.Count() }).ToListAsync(cancellationToken);
        var included = totals.SingleOrDefault(x => x.Currency == budget.Currency);
        var items = await query.Where(x => x.Currency == budget.Currency).OrderByDescending(x => x.PostedAt).ThenBy(x => x.Id).Take(5)
            .Select(x => new { x.Id, accountName = x.Account == null ? "Account" : x.Account.CustomName ?? x.Account.Name,
                x.PostedAt, x.Description, x.MerchantName, x.Amount, x.Currency }).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Results.Ok(new { period.From, period.To, budget.Currency, spent = included?.Spent ?? 0,
            transactionCount = included?.Count ?? 0, observedThrough = period.From > today ? (DateOnly?)null : period.To < today ? period.To : today,
            excludedCurrencies = totals.Where(x => x.Currency != budget.Currency).OrderBy(x => x.Currency)
                .Select(x => new ExcludedCurrencyResponse(x.Currency, x.Count)), items });
    }

    private static async Task<IResult> GetBudgets(TenantResolver tenantResolver, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var snapshot = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var budgets = await Definitions(dbContext, tenant.TenantId).AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        return Results.Ok(budgets.Select(x => ToResponse(x)).ToList());
    }

    private static async Task<IResult> CreateBudget(BudgetRequest request, TenantResolver tenantResolver, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var error = Validate(request);
        if (error != null)
        {
            return Results.BadRequest(error);
        }
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        error = await ValidateReferences(dbContext, tenant.TenantId, request, cancellationToken);
        if (error != null)
        {
            return Results.BadRequest(error);
        }
        if (await dbContext.Budgets.CountAsync(x => x.TenantId == tenant.TenantId, cancellationToken) >= 100)
        {
            return Results.BadRequest("A family can have up to 100 budgets.");
        }
        var budget = new Budget { TenantId = tenant.TenantId, Name = request.Name!.Trim(), CreatedAt = DateTimeOffset.UtcNow };
        Apply(budget, request);
        dbContext.Budgets.Add(budget);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/budgets/{budget.Id}", ToResponse(budget));
    }

    private static async Task<IResult> UpdateBudget(Guid budgetId, BudgetRequest request, TenantResolver tenantResolver, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var error = Validate(request);
        if (error != null || request.ExpectedVersion is null or < 0)
        {
            return Results.BadRequest(error ?? "The current budget version is required.");
        }
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var budget = await Definitions(dbContext, tenant.TenantId).SingleOrDefaultAsync(x => x.Id == budgetId, cancellationToken);
        if (budget is null)
        {
            return Results.NotFound();
        }
        if (budget.Version != request.ExpectedVersion)
        {
            return Results.Conflict("This budget changed. Reload it before saving.");
        }
        error = await ValidateReferences(dbContext, tenant.TenantId, request, cancellationToken);
        if (error != null)
        {
            return Results.BadRequest(error);
        }
        Apply(budget, request);
        budget.Version++;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict("This budget changed. Reload it before saving.");
        }
        return Results.Ok(ToResponse(budget));
    }

    private static async Task<IResult> DeleteBudget(Guid budgetId, int? expectedVersion, TenantResolver tenantResolver, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        if (expectedVersion is null or < 0)
        {
            return Results.BadRequest("The current budget version is required.");
        }
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var budget = await dbContext.Budgets.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == budgetId, cancellationToken);
        if (budget is null)
        {
            return Results.NotFound();
        }
        if (budget.Version != expectedVersion)
        {
            return Results.Conflict("This budget changed. Reload it before deleting.");
        }
        dbContext.Budgets.Remove(budget);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict("This budget changed. Reload it before deleting.");
        }
        return Results.NoContent();
    }

    private static async Task<IResult> GetPeriods(Guid budgetId, DateOnly? date, int? count, TenantResolver tenantResolver, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var selectedDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var periodCount = count ?? 6;
        if (!BudgetPeriods.SupportedDate(selectedDate) || periodCount is < 1 or > 12)
        {
            return Results.BadRequest("Use a date from 1901 through 9990 and between 1 and 12 periods.");
        }
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var snapshot = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var budget = await Definitions(dbContext, tenant.TenantId).AsNoTracking().SingleOrDefaultAsync(x => x.Id == budgetId, cancellationToken);
        if (budget is null)
        {
            return Results.NotFound();
        }
        var query = await BudgetQueries.Transactions(dbContext, budget, cancellationToken, includeOtherCurrencies: true);
        var history = BudgetPeriods.History(budget.Frequency, budget.AnchorDate, selectedDate, periodCount).ToList();
        var from = new DateTimeOffset(history.Min(x => x.From).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(history.Max(x => x.EndExclusive).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        // One SQL aggregation across the bounded history, never a materialized transaction ledger.
        var daily = await query.Where(x => x.PostedAt >= from && x.PostedAt < to)
            .GroupBy(x => new { x.PostedAt!.Value.Year, x.PostedAt.Value.Month, x.PostedAt.Value.Day, x.Currency })
            .Select(x => new { x.Key.Year, x.Key.Month, x.Key.Day, x.Key.Currency, Spent = x.Sum(y => -y.Amount), Count = x.Count() })
            .ToListAsync(cancellationToken);
        var periods = new List<PeriodResponse>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var period in history)
        {
            var days = daily.Where(x => new DateOnly(x.Year, x.Month, x.Day) >= period.From && new DateOnly(x.Year, x.Month, x.Day) <= period.To).ToList();
            var included = days.Where(x => x.Currency == budget.Currency).ToList();
            var spent = included.Sum(x => x.Spent);
            var excluded = days.Where(x => x.Currency != budget.Currency).GroupBy(x => x.Currency)
                .Select(x => new ExcludedCurrencyResponse(x.Key, x.Sum(y => y.Count))).OrderBy(x => x.Currency).ToList();
            periods.Add(new PeriodResponse(period.From, period.To, budget.Limit, spent, budget.Limit - spent,
                Math.Round(spent / budget.Limit * 100, 1), included.Sum(x => x.Count),
                period.From > today ? null : period.To < today ? period.To : today, excluded));
        }
        return Results.Ok(new { budgetId, budget.Version, budget.Currency, date = selectedDate, periods });
    }

    private static async Task<IResult> GetTransactions(Guid budgetId, DateOnly date, int? page, int? pageSize, TenantResolver tenantResolver, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var currentPage = page ?? 1;
        var size = pageSize ?? 25;
        if (!BudgetPeriods.SupportedDate(date) || currentPage < 1 || size is < 1 or > 100 || (long)(currentPage - 1) * size > int.MaxValue)
        {
            return Results.BadRequest("Use a date from 1901 through 9990, a positive page and a page size from 1 to 100.");
        }
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var snapshot = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var budget = await Definitions(dbContext, tenant.TenantId).AsNoTracking().SingleOrDefaultAsync(x => x.Id == budgetId, cancellationToken);
        if (budget is null)
        {
            return Results.NotFound();
        }
        var period = BudgetPeriods.Containing(budget.Frequency, budget.AnchorDate, date);
        var query = (await BudgetQueries.Transactions(dbContext, budget, cancellationToken)).InPeriod(period);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.PostedAt).ThenBy(x => x.Id)
            .Skip((currentPage - 1) * size).Take(size)
            .Select(x => new
            {
                x.Id, x.AccountId, accountName = x.Account == null ? "Account" : x.Account.CustomName ?? x.Account.Name,
                x.PostedAt, x.Description, x.MerchantName, x.PrimaryCategory, x.SecondaryCategory, x.Amount, x.Currency
            }).ToListAsync(cancellationToken);
        return Results.Ok(new { budgetId, budget.Version, period.From, period.To, page = currentPage, pageSize = size, totalCount, items });
    }

    private static IQueryable<Budget> Definitions(FinyteDbContext dbContext, Guid tenantId) =>
        dbContext.Budgets.Where(x => x.TenantId == tenantId).Include(x => x.Tags).Include(x => x.Accounts).AsSplitQuery();

    private static string? Validate(BudgetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            return "Use a budget name between 1 and 120 characters.";
        }
        if (request.Limit <= 0 || request.Limit > 9999999999999999.99m || decimal.Round(request.Limit, 2) != request.Limit)
        {
            return "The limit must be positive, have at most two decimal places and fit within 16 whole digits.";
        }
        if (request.Currency is null || request.Currency.Trim().Length != 3 || !request.Currency.Trim().All(x => char.IsAsciiLetter(x)))
        {
            return "Use a three-letter currency code.";
        }
        if (request.Frequency is not ("weekly" or "fortnightly" or "monthly") || !BudgetPeriods.SupportedDate(request.AnchorDate))
        {
            return "Choose weekly, fortnightly or monthly with an anchor date from 1901 through 9990.";
        }
        if (request.MatchMode is not ("all" or "selected") || request.AccountScope is not ("analytics" or "selected"))
        {
            return "Choose all or selected spending and analytics or selected accounts.";
        }
        if (request.Categories is null || request.TagIds is null || request.AccountIds is null
            || request.Categories.Length > 50 || request.TagIds.Length > 50 || request.AccountIds.Length > 100
            || request.Categories.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 120)
            || request.TagIds.Contains(Guid.Empty) || request.AccountIds.Contains(Guid.Empty))
        {
            return "Provide arrays with up to 50 categories/tags and 100 accounts. Categories must contain 1 to 120 characters and IDs must be nonempty.";
        }
        if (request.MatchMode == "selected" && request.Categories.Length == 0 && request.TagIds.Length == 0)
        {
            return "Select at least one exact category or tag, or explicitly choose all spending.";
        }
        if (request.MatchMode == "all" && (request.Categories.Length > 0 || request.TagIds.Length > 0)
            || request.AccountScope == "analytics" && request.AccountIds.Length > 0
            || request.AccountScope == "selected" && request.AccountIds.Length == 0)
        {
            return "Selections must agree with the spending and account scopes.";
        }
        return null;
    }

    private static async Task<string?> ValidateReferences(FinyteDbContext dbContext, Guid tenantId, BudgetRequest request, CancellationToken cancellationToken)
    {
        if (request.Categories!.Length > 0)
        {
            var available = await AvailableCategories(dbContext, tenantId, cancellationToken);
            if (request.Categories.Any(x => !available.Contains(x.Trim(), StringComparer.OrdinalIgnoreCase)))
            {
                return "One or more selected categories are no longer available. Remove them and choose from the current categories.";
            }
        }
        var tagIds = request.TagIds!.Distinct().ToArray();
        var accountIds = request.AccountIds!.Distinct().ToArray();
        if (await dbContext.TransactionTags.CountAsync(x => x.TenantId == tenantId && tagIds.Contains(x.Id), cancellationToken) != tagIds.Length
            || await dbContext.Accounts.CountAsync(x => x.TenantId == tenantId && accountIds.Contains(x.Id), cancellationToken) != accountIds.Length)
        {
            return "One or more selected tags or accounts do not exist in this family.";
        }
        return null;
    }

    private static void Apply(Budget budget, BudgetRequest request)
    {
        budget.Name = request.Name!.Trim();
        budget.Limit = request.Limit;
        budget.Currency = request.Currency!.Trim().ToUpperInvariant();
        budget.Frequency = request.Frequency!;
        budget.AnchorDate = request.AnchorDate;
        budget.MatchMode = request.MatchMode!;
        budget.Categories = request.Categories!.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        budget.AccountScope = request.AccountScope!;
        budget.UpdatedAt = DateTimeOffset.UtcNow;
        var tagIds = request.TagIds!.Distinct().ToHashSet();
        foreach (var tag in budget.Tags.Where(x => !tagIds.Contains(x.TagId)).ToList())
        {
            budget.Tags.Remove(tag);
        }
        var existingTags = budget.Tags.Select(x => x.TagId).ToHashSet();
        foreach (var tagId in tagIds.Where(x => !existingTags.Contains(x)))
        {
            budget.Tags.Add(new BudgetTag { BudgetId = budget.Id, TagId = tagId });
        }
        var accountIds = request.AccountIds!.Distinct().ToHashSet();
        foreach (var account in budget.Accounts.Where(x => !accountIds.Contains(x.AccountId)).ToList())
        {
            budget.Accounts.Remove(account);
        }
        var existingAccounts = budget.Accounts.Select(x => x.AccountId).ToHashSet();
        foreach (var accountId in accountIds.Where(x => !existingAccounts.Contains(x)))
        {
            budget.Accounts.Add(new BudgetAccount { BudgetId = budget.Id, AccountId = accountId });
        }
    }

    private static object ToResponse(Budget budget) => new
    {
        budget.Id, budget.Name, budget.Limit, budget.Currency, budget.Frequency, budget.AnchorDate, budget.MatchMode,
        budget.Categories, budget.AccountScope, tagIds = budget.Tags.Select(x => x.TagId).OrderBy(x => x).ToArray(),
        accountIds = budget.Accounts.Select(x => x.AccountId).OrderBy(x => x).ToArray(), budget.Version, budget.CreatedAt, budget.UpdatedAt
    };

    public sealed record BudgetRequest(string? Name, decimal Limit, string? Currency, string? Frequency, DateOnly AnchorDate,
        string? MatchMode, string[]? Categories, Guid[]? TagIds, string? AccountScope, Guid[]? AccountIds, int? ExpectedVersion);
    private sealed record ExcludedCurrencyResponse(string Currency, int TransactionCount);
    private sealed record PeriodResponse(DateOnly From, DateOnly To, decimal Limit, decimal Spent, decimal Remaining, decimal UsedPercent, int TransactionCount, DateOnly? ObservedThrough, IReadOnlyList<ExcludedCurrencyResponse> ExcludedCurrencies);
}
