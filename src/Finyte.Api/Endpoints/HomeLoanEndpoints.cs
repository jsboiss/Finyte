using System.Text;
using Finyte.Api.Tenancy;
using Finyte.Core.HomeLoans;
using Finyte.Data;
using Finyte.Data.Billing;
using Finyte.Data.HomeLoans;
using Finyte.Data.Tagging;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class HomeLoanEndpoints
{
    public static IEndpointRouteBuilder MapHomeLoanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/home-loan").RequireAuthorization();
        group.MapGet("/", GetReport).WithName("GetHomeLoanReport");
        group.MapPut("/setup", SaveSetup).WithName("SaveHomeLoanSetup");
        group.MapPut("/income-sources", SaveIncomeSource).WithName("SaveHomeLoanIncomeSource");
        group.MapPut("/categories", SaveCategory).WithName("SaveHomeLoanCategory");
        group.MapGet("/export/{file}", Export).WithName("ExportHomeLoan");
        group.MapPut("/payment-category", SavePaymentCategory).WithName("SaveHomeLoanPaymentCategory");
        return app;
    }

    private static async Task<IResult> GetReport(int? months, string? end, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, HomeLoanQueries queries, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        if (Period(months, end) is not { } period)
        {
            return InvalidPeriod();
        }
        return Results.Ok(await queries.GetReport(tenant.TenantId, period.Months, period.End, cancellationToken));
    }

    private static async Task<IResult> Export(string file, int? months, string? end, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, HomeLoanQueries queries, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        if (file is not ("summary" or "transactions"))
        {
            return Results.NotFound();
        }
        if (Period(months, end) is not { } period)
        {
            return InvalidPeriod();
        }
        var (report, rows) = await queries.GetReportWithRows(tenant.TenantId, period.Months, period.End, cancellationToken);
        var csv = file == "summary" ? HomeLoanCsv.Summary(report) : HomeLoanCsv.Transactions(report, rows);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", $"home-loan-{file}-{report.FromMonth}-to-{report.ToMonth}.csv");
    }

    private static async Task<IResult> SaveSetup(SaveSetupRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        var first = request.FirstApplicant?.Trim();
        var second = string.IsNullOrWhiteSpace(request.SecondApplicant) ? null : request.SecondApplicant.Trim();
        var roles = request.Accounts ?? [];
        if (string.IsNullOrEmpty(first) || first.Length > 60 || second?.Length > 60 || roles.Length > 200
            || roles.Any(x => x is null || x.AccountId == Guid.Empty || !HomeLoanQueries.Roles.Contains(x.Role))
            || roles.Select(x => x.AccountId).Distinct().Count() != roles.Length || request.ExpectedVersion is null or < 0)
        {
            return Results.BadRequest("Provide a first applicant name (up to 60 characters), an optional second name, the current version, and a spending, savings or excluded role for each account at most once.");
        }
        var ids = roles.Select(x => x.AccountId).ToArray();
        if (await dbContext.Accounts.CountAsync(x => x.TenantId == tenant.TenantId && ids.Contains(x.Id), cancellationToken) != ids.Length)
        {
            return Results.BadRequest("Every account must belong to this family.");
        }
        var now = timeProvider.GetUtcNow();
        var profile = await dbContext.HomeLoanProfiles.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, cancellationToken);
        if ((profile?.Version ?? 0) != request.ExpectedVersion)
        {
            return Stale();
        }
        if (profile is null)
        {
            profile = new HomeLoanProfile { TenantId = tenant.TenantId, FirstApplicant = first, CreatedAt = now };
            dbContext.HomeLoanProfiles.Add(profile);
        }
        profile.FirstApplicant = first;
        profile.SecondApplicant = second;
        profile.SpendingAccountIds = roles.Where(x => x.Role == "spending").Select(x => x.AccountId).ToArray();
        profile.SavingsAccountIds = roles.Where(x => x.Role == "savings").Select(x => x.AccountId).ToArray();
        profile.Version++;
        profile.UpdatedAt = now;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Stale();
        }
        return Results.Ok(new { profile.Version });
    }

    private static async Task<IResult> SaveIncomeSource(SaveIncomeSourceRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        var key = MerchantTagMatcher.Normalize(request.Key);
        var previous = MerchantTagMatcher.Normalize(request.PreviousKey);
        var name = request.Name?.Trim() ?? "";
        if (key.Length is 0 or > 200 || name.Length is 0 or > 256
            || request.Kind is not null && !HomeLoanCatalog.IncomeKinds.Contains(request.Kind) || request.Applicant is not (null or 1 or 2))
        {
            return Results.BadRequest("Provide the text payments start with (up to 200 characters), a name, an income type (salary, other-income, refund, gift or not-income) or none, and applicant 1, 2 or none.");
        }
        if (previous.Length > 0 && previous != key)
        {
            dbContext.HomeLoanIncomeSources.RemoveRange(dbContext.HomeLoanIncomeSources.Where(x => x.TenantId == tenant.TenantId && x.MatchKey == previous));
        }
        var source = await dbContext.HomeLoanIncomeSources.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.MatchKey == key, cancellationToken);
        if (request.Kind is null)
        {
            if (source is not null)
            {
                dbContext.HomeLoanIncomeSources.Remove(source);
            }
        }
        else
        {
            if (source is null)
            {
                source = new HomeLoanIncomeSource { TenantId = tenant.TenantId, MatchKey = key, Name = name, Kind = request.Kind };
                dbContext.HomeLoanIncomeSources.Add(source);
            }
            source.Name = name;
            source.Kind = request.Kind;
            source.Applicant = request.Kind is "salary" or "other-income" ? request.Applicant : null;
            source.UpdatedAt = timeProvider.GetUtcNow();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SaveCategory(SaveCategoryRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        if (request.TagId is not { } tagId || request.Category is not null && HomeLoanCatalog.Category(request.Category) is null)
        {
            return Results.BadRequest("Provide a tag and a lender category, or no category to clear it.");
        }
        if (!await dbContext.TransactionTags.AnyAsync(x => x.TenantId == tenant.TenantId && x.Id == tagId, cancellationToken))
        {
            return Results.NotFound();
        }
        var choice = await dbContext.HomeLoanCategoryChoices.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.TagId == tagId, cancellationToken);
        if (request.Category is null)
        {
            if (choice is not null)
            {
                dbContext.HomeLoanCategoryChoices.Remove(choice);
            }
        }
        else
        {
            if (choice is null)
            {
                choice = new HomeLoanCategoryChoice { TenantId = tenant.TenantId, TagId = tagId, Category = request.Category };
                dbContext.HomeLoanCategoryChoices.Add(choice);
            }
            choice.Category = request.Category;
            choice.UpdatedAt = timeProvider.GetUtcNow();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SavePaymentCategory(SavePaymentCategoryRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        if (request.TransactionId is not { } transactionId || request.Category is not null && HomeLoanCatalog.Category(request.Category) is null)
        {
            return Results.BadRequest("Provide a payment and a lender category, or no category to use its tags again.");
        }
        if (!await dbContext.Transactions.AnyAsync(x => x.TenantId == tenant.TenantId && x.Id == transactionId, cancellationToken))
        {
            return Results.NotFound();
        }
        var choice = await dbContext.HomeLoanPaymentCategories.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.TransactionId == transactionId, cancellationToken);
        if (request.Category is null)
        {
            if (choice is not null)
            {
                dbContext.HomeLoanPaymentCategories.Remove(choice);
            }
        }
        else
        {
            if (choice is null)
            {
                choice = new HomeLoanPaymentCategory { TenantId = tenant.TenantId, TransactionId = transactionId, Category = request.Category };
                dbContext.HomeLoanPaymentCategories.Add(choice);
            }
            choice.Category = request.Category;
            choice.UpdatedAt = timeProvider.GetUtcNow();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static (int Months, DateOnly? End)? Period(int? months, string? end)
    {
        var length = months ?? 3;
        if (!HomeLoanQueries.PeriodLengths.Contains(length))
        {
            return null;
        }
        if (string.IsNullOrEmpty(end))
        {
            return (length, null);
        }
        return HomeLoanQueries.TryParseMonth(end, out var month) ? (length, month) : null;
    }

    private static IResult InvalidPeriod() => Results.BadRequest("Choose 3, 6 or 12 months and an end month written as YYYY-MM.");
    private static IResult SubscriptionRequired() => Results.Problem("An active subscription is required for the home loan summary.", statusCode: StatusCodes.Status402PaymentRequired);
    private static IResult Stale() => Results.Conflict("The home loan setup changed. Reload it before saving.");

    private sealed record AccountRoleRequest(Guid AccountId, string Role);
    private sealed record SaveSetupRequest(string? FirstApplicant, string? SecondApplicant, AccountRoleRequest[]? Accounts, int? ExpectedVersion);
    private sealed record SaveIncomeSourceRequest(string? Key, string? Name, string? Kind, int? Applicant, string? PreviousKey);
    private sealed record SaveCategoryRequest(Guid? TagId, string? Category);
    private sealed record SavePaymentCategoryRequest(Guid? TransactionId, string? Category);
}
