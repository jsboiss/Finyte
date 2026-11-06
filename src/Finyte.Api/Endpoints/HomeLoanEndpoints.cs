using System.Text;
using Finyte.Api.Tenancy;
using Finyte.Core.Accounts;
using Finyte.Core.HomeLoans;
using Finyte.Data;
using Finyte.Data.Analytics;
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
        group.MapPut("/merchant-tags", TagMerchant).WithName("TagHomeLoanMerchant");
        group.MapPut("/payment-tag", TagPayment).WithName("TagHomeLoanPayment");
        group.MapPut("/merchant-tags/undo", UndoMerchantTag).WithName("UndoHomeLoanMerchantTag");
        group.MapPut("/payment-tag/undo", UndoPaymentTag).WithName("UndoHomeLoanPaymentTag");
        app.MapPut("/api/merchant-vendors", SaveVendors).RequireAuthorization().WithName("SaveMerchantVendors");
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
        var key = request.Key?.Trim() ?? "";
        var name = request.Name?.Trim() ?? "";
        if (key.Length is 0 or > 200 || name.Length is 0 or > 256
            || request.Kind is not null && !HomeLoanCatalog.IncomeKinds.Contains(request.Kind) || request.Applicant is not (null or 1 or 2))
        {
            return Results.BadRequest("Provide the payer key and name, an income type (salary, other-income, refund, gift or not-income) or none, and applicant 1, 2 or none.");
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
            return Results.BadRequest("Provide a tag and a lender category, or no category to use the default.");
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

    private static async Task<IResult> SaveVendors(SaveVendorsRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        FinyteDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var items = request.Assignments ?? [];
        if (items.Length is 0 or > 200 || items.Any(x => x is null || MerchantTagMatcher.Normalize(x.StatementName).Length is 0 or > 256
            || x.Vendor is not null && x.Vendor.Trim().Length is 0 or > 120))
        {
            return Results.BadRequest("Provide 1–200 statement names, each with a vendor name up to 120 characters, or no vendor to clear it.");
        }
        var keys = items.Select(x => MerchantTagMatcher.Normalize(x.StatementName)).Distinct().ToList();
        var existing = await dbContext.MerchantVendors.Where(x => x.TenantId == tenant.TenantId && keys.Contains(x.MatchKey)).ToDictionaryAsync(x => x.MatchKey, cancellationToken);
        foreach (var item in items.DistinctBy(x => MerchantTagMatcher.Normalize(x.StatementName)))
        {
            var key = MerchantTagMatcher.Normalize(item.StatementName);
            var vendor = existing.GetValueOrDefault(key);
            if (item.Vendor is null)
            {
                if (vendor is not null)
                {
                    dbContext.MerchantVendors.Remove(vendor);
                }
                continue;
            }
            if (vendor is null)
            {
                vendor = new MerchantVendor { TenantId = tenant.TenantId, MatchKey = key, Vendor = item.Vendor.Trim() };
                dbContext.MerchantVendors.Add(vendor);
            }
            vendor.Vendor = item.Vendor.Trim();
            vendor.UpdatedAt = timeProvider.GetUtcNow();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TagMerchant(TagMerchantRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TransactionTagService tagService, IProjectionInvalidator projectionInvalidator, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        var names = (request.StatementNames ?? []).Select(x => x?.Trim() ?? "").Distinct().ToList();
        if (names.Count is 0 or > 200 || names.Any(x => x.Length > 256 || MerchantTagMatcher.Normalize(x).Length == 0))
        {
            return Results.BadRequest("Provide 1–200 statement names of up to 256 characters.");
        }
        await using var databaseTransaction = await tagService.BeginMutation(tenant.TenantId, cancellationToken);
        if (await ResolveTag(tenant.TenantId, request.TagId, request.TagName, dbContext, cancellationToken) is not { } tag)
        {
            return Results.BadRequest("Choose one of your tags, or a new tag name up to 80 characters.");
        }
        var rules = await tagService.GetRules(tenant.TenantId, cancellationToken);
        var keys = names.Select(MerchantTagMatcher.Normalize).Distinct().ToList();
        foreach (var name in names.DistinctBy(MerchantTagMatcher.Normalize))
        {
            var key = MerchantTagMatcher.Normalize(name);
            if (!rules.Any(x => x.MerchantKey == key && x.TagId == tag.Id))
            {
                dbContext.MerchantTagRules.Add(new MerchantTagRule { TenantId = tenant.TenantId, MerchantName = name, MerchantKey = key, TagId = tag.Id, Tag = tag, CreatedAt = DateTimeOffset.UtcNow });
            }
        }
        var chosen = await dbContext.Transactions.Where(x => x.TenantId == tenant.TenantId && x.TagAssignments.Any(y => y.Source == TransactionTagSource.Manual))
            .Include(x => x.TagAssignments).Include(x => x.TagExclusions).ToListAsync(cancellationToken);
        foreach (var transaction in chosen.Where(x => keys.Any(key => MerchantTagMatcher.Matches(x.MerchantName, x.Description, key))
            && x.TagAssignments.All(y => y.TagId != tag.Id) && x.TagExclusions.All(y => y.TagId != tag.Id)))
        {
            transaction.TagExclusions.Add(new TransactionTagExclusion { TransactionId = transaction.Id, TagId = tag.Id, CreatedAt = DateTimeOffset.UtcNow });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await tagService.ReconcileTenant(tenant.TenantId, cancellationToken);
        await projectionInvalidator.TenantProjectionDataChanged(tenant.TenantId, "home loan merchant tagged", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        return Results.Ok(new { tag.Id, tag.Name });
    }

    private static async Task<IResult> TagPayment(TagPaymentRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TransactionTagService tagService, IProjectionInvalidator projectionInvalidator, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        await using var databaseTransaction = await tagService.BeginMutation(tenant.TenantId, cancellationToken);
        var transaction = await dbContext.Transactions.Where(x => x.TenantId == tenant.TenantId && x.Id == request.TransactionId)
            .Include(x => x.TagAssignments).Include(x => x.TagExclusions).SingleOrDefaultAsync(cancellationToken);
        if (transaction is null)
        {
            return Results.NotFound();
        }
        if (await ResolveTag(tenant.TenantId, request.TagId, request.TagName, dbContext, cancellationToken) is not { } tag)
        {
            return Results.BadRequest("Choose one of your tags, or a new tag name up to 80 characters.");
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        tagService.SetTags(transaction, new HashSet<Guid> { tag.Id }, new HashSet<Guid> { tag.Id });
        await projectionInvalidator.TransactionChanged(tenant.TenantId, transaction.AccountId, transaction.PostedAt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        return Results.Ok(new { tag.Id, tag.Name });
    }

    private static async Task<IResult> UndoMerchantTag(UndoMerchantTagRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TransactionTagService tagService, IProjectionInvalidator projectionInvalidator, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        var keys = (request.StatementNames ?? []).Select(MerchantTagMatcher.Normalize).Where(x => x.Length > 0).Distinct().ToList();
        if (keys.Count is 0 or > 200 || request.TagId is not { } tagId)
        {
            return Results.BadRequest("Provide the tag and 1–200 statement names to undo.");
        }
        await using var databaseTransaction = await tagService.BeginMutation(tenant.TenantId, cancellationToken);
        var rules = await tagService.GetRules(tenant.TenantId, cancellationToken);
        foreach (var rule in rules.Where(x => x.TagId == tagId && keys.Contains(x.MerchantKey)).ToList())
        {
            dbContext.MerchantTagRules.Remove(rule);
        }
        var excluded = await dbContext.Transactions.Where(x => x.TenantId == tenant.TenantId && x.TagExclusions.Any(y => y.TagId == tagId))
            .Include(x => x.TagExclusions).ToListAsync(cancellationToken);
        foreach (var transaction in excluded.Where(x => keys.Any(key => MerchantTagMatcher.Matches(x.MerchantName, x.Description, key))))
        {
            dbContext.TransactionTagExclusions.RemoveRange(transaction.TagExclusions.Where(x => x.TagId == tagId));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await tagService.ReconcileTenant(tenant.TenantId, cancellationToken);
        await projectionInvalidator.TenantProjectionDataChanged(tenant.TenantId, "home loan merchant tag undone", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> UndoPaymentTag(UndoPaymentTagRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TransactionTagService tagService, IProjectionInvalidator projectionInvalidator, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        await using var databaseTransaction = await tagService.BeginMutation(tenant.TenantId, cancellationToken);
        var transaction = await dbContext.Transactions.Where(x => x.TenantId == tenant.TenantId && x.Id == request.TransactionId)
            .Include(x => x.TagAssignments).Include(x => x.TagExclusions).SingleOrDefaultAsync(cancellationToken);
        if (transaction is null)
        {
            return Results.NotFound();
        }
        foreach (var assignment in transaction.TagAssignments.Where(x => x.TagId == request.TagId && x.Source == TransactionTagSource.Manual).ToList())
        {
            transaction.TagAssignments.Remove(assignment);
            dbContext.TransactionTagAssignments.Remove(assignment);
        }
        tagService.Reconcile(transaction, await tagService.GetRules(tenant.TenantId, cancellationToken));
        await projectionInvalidator.TransactionChanged(tenant.TenantId, transaction.AccountId, transaction.PostedAt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        return Results.NoContent();
    }
    private static async Task<TransactionTag?> ResolveTag(Guid tenantId, Guid? tagId, string? tagName, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        if (tagId is { } id)
        {
            return await dbContext.TransactionTags.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        }
        var name = tagName?.Trim() ?? "";
        if (name.Length is 0 or > 80)
        {
            return null;
        }
        var tags = await dbContext.TransactionTags.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var existing = tags.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }
        var starter = MerchantKeywordCatalog.Starter(name);
        var tag = new TransactionTag { TenantId = tenantId, Name = starter?.Name ?? name, Color = starter?.Color ?? "#64748b", CreatedAt = DateTimeOffset.UtcNow };
        dbContext.TransactionTags.Add(tag);
        return tag;
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
    private sealed record SaveIncomeSourceRequest(string? Key, string? Name, string? Kind, int? Applicant);
    private sealed record SaveCategoryRequest(Guid? TagId, string? Category);
    private sealed record VendorAssignment(string StatementName, string? Vendor);
    private sealed record SaveVendorsRequest(VendorAssignment[]? Assignments);
    private sealed record TagMerchantRequest(string[]? StatementNames, Guid? TagId, string? TagName);
    private sealed record TagPaymentRequest(Guid TransactionId, Guid? TagId, string? TagName);
    private sealed record UndoMerchantTagRequest(string[]? StatementNames, Guid? TagId);
    private sealed record UndoPaymentTagRequest(Guid TransactionId, Guid TagId);
}
