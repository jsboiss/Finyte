using System.Text.RegularExpressions;
using Finyte.Api.Tenancy;
using Finyte.Core.Accounts;
using Finyte.Data;
using Finyte.Data.Analytics;
using Finyte.Data.Billing;
using Finyte.Data.Transfers;
using Finyte.Data.Tagging;
using Finyte.Data.Transactions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static partial class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var transactions = app.MapGroup("/api/transactions").RequireAuthorization();
        transactions.MapGet("/", GetTransactions).WithName("GetTransactions");
        transactions.MapPut("/{transactionId:guid}/tags", SetTransactionTags).WithName("SetTransactionTags");
        transactions.MapPost("/{transactionId:guid}/tags/restore-automatic", RestoreAutomaticTags).WithName("RestoreAutomaticTransactionTags");

        var tags = app.MapGroup("/api/tags").RequireAuthorization();
        tags.MapGet("/", GetTags).WithName("GetTransactionTags");
        tags.MapPost("/", CreateTag).WithName("CreateTransactionTag");
        tags.MapDelete("/{tagId:guid}", DeleteTag).WithName("DeleteTransactionTag");

        var merchantTags = app.MapGroup("/api/merchant-tags").RequireAuthorization();
        merchantTags.MapGet("/", GetMerchantRules).WithName("GetMerchantTagRules");
        merchantTags.MapPost("/", CreateMerchantRule).WithName("CreateMerchantTagRule");
        merchantTags.MapPut("/{ruleId:guid}", UpdateMerchantRule).WithName("UpdateMerchantTagRule");
        merchantTags.MapDelete("/{ruleId:guid}", DeleteMerchantRule).WithName("DeleteMerchantTagRule");

        return app;
    }

    private static async Task<IResult> GetTransactions(
        int? page,
        int? pageSize,
        Guid? accountId,
        DateOnly? from,
        DateOnly? to,
        string? search,
        string? category,
        [FromQuery] Guid[]? tagIds,
        string? tagMatch,
        bool? untagged,
        decimal? minAmount,
        decimal? maxAmount,
        string? currency,
        string? sort,
        bool? postedOnly,
        bool? analyticsOnly,
        string? direction,
        string? internalTransfers,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);

        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return TypedResults.Problem("An active subscription is required to view transactions.", statusCode: StatusCodes.Status402PaymentRequired);
        }

        var filters = new TransactionSearch
        {
            Page = page ?? 1,
            PageSize = pageSize ?? 25,
            AccountId = accountId,
            From = from,
            To = to,
            Search = search?.Trim(),
            Category = category?.Trim(),
            TagIds = tagIds ?? [],
            TagMatch = tagMatch ?? "any",
            Untagged = untagged ?? false,
            MinAmount = minAmount,
            MaxAmount = maxAmount,
            Currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant(),
            Sort = sort ?? "-date",
            PostedOnly = postedOnly ?? false,
            AnalyticsOnly = analyticsOnly ?? false,
            Direction = direction ?? "all",
            InternalTransfers = internalTransfers ?? "include"
        };
        var errors = filters.Validate();

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var currentPage = filters.Page;
        var take = filters.PageSize;
        var transfers = dbContext.ValidConfirmedTransfers(currentTenant.TenantId);
        var query = filters.Apply(dbContext.Transactions.AsNoTracking(), currentTenant.TenantId);
        if (filters.AnalyticsOnly && filters.AccountId == null)
        {
            var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == currentTenant.TenantId).ToListAsync(cancellationToken);
            var accountIds = accounts.Where(x => AccountPreferences.IncludeInAnalytics(x)).Select(x => x.Id).ToArray();
            query = query.Where(x => accountIds.Contains(x.AccountId));
        }
        if (filters.InternalTransfers == "exclude")
        {
            query = query.ExcludeInternalTransfers(dbContext, currentTenant.TenantId);
        }
        else if (filters.InternalTransfers == "only")
        {
            query = query.Where(x => transfers.Any(y => y.DebitTransactionId == x.Id || y.CreditTransactionId == x.Id));
        }
        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await filters.Order(query)
            .Skip((currentPage - 1) * take)
            .Take(take)
            .Select(x => new TransactionResponse(
                x.Id,
                x.AccountId,
                x.Account == null ? "Account" : x.Account.CustomName ?? x.Account.Name,
                GetPostedDate(x.PostedAt ?? x.CreatedAt),
                x.Description ?? "",
                x.MerchantName,
                GetCategory(x.PrimaryCategory, x.SecondaryCategory),
                ToMinorUnits(x.Amount),
                x.Currency,
                transfers.Any(y => y.DebitTransactionId == x.Id || y.CreditTransactionId == x.Id),
                x.TagAssignments
                    .Where(y => y.Tag != null && y.Tag.TenantId == currentTenant.TenantId)
                    .OrderBy(y => y.Tag == null ? "" : y.Tag.Name)
                    .Select(y => new TransactionTagResponse(y.TagId, y.Tag == null ? "" : y.Tag.Name, y.Tag == null ? "#64748b" : y.Tag.Color,
                        y.Source, y.MerchantRuleId, y.MerchantRule == null ? null : y.MerchantRule.MerchantName))
                    .ToList(),
                x.TagExclusions.Select(y => y.TagId).ToList()))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new TransactionPageResponse(transactions, currentPage, take, totalCount));
    }

    private static async Task<Ok<IReadOnlyList<TransactionTagResponse>>> GetTags(
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var tags = await dbContext.TransactionTags
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId)
            .OrderBy(x => x.Name)
            .Select(x => new TransactionTagResponse(x.Id, x.Name, x.Color))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<TransactionTagResponse>>(tags);
    }

    private static async Task<Results<Created<TransactionTagResponse>, BadRequest<string>, Conflict<string>>> CreateTag(
        CreateTransactionTagRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest("Tag name is required.");
        }

        var name = request.Name.Trim();
        var color = NormalizeColor(request.Color);
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var exists = await dbContext.TransactionTags
            .AnyAsync(x => x.TenantId == currentTenant.TenantId && x.Name == name, cancellationToken);

        if (exists)
        {
            return TypedResults.Conflict("A tag with this name already exists.");
        }

        var tag = new TransactionTag
        {
            TenantId = currentTenant.TenantId,
            Name = name,
            Color = color,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TransactionTags.Add(tag);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new TransactionTagResponse(tag.Id, tag.Name, tag.Color);
        return TypedResults.Created($"/api/tags/{tag.Id}", response);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteTag(
        Guid tagId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
        TransactionTagService tagService,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var databaseTransaction = await tagService.BeginMutation(currentTenant.TenantId, cancellationToken);
        var tag = await dbContext.TransactionTags
            .SingleOrDefaultAsync(x => x.Id == tagId && x.TenantId == currentTenant.TenantId, cancellationToken);

        if (tag is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.TransactionTags.Remove(tag);
        await projectionInvalidator.TenantProjectionDataChanged(currentTenant.TenantId, "transaction tag deleted", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<TransactionTagResponse>>, NotFound, BadRequest<string>>> SetTransactionTags(
        Guid transactionId,
        SetTransactionTagsRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
        TransactionTagService tagService,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var databaseTransaction = await tagService.BeginMutation(currentTenant.TenantId, cancellationToken);
        var transaction = await dbContext.Transactions
            .Where(x => x.Id == transactionId && x.TenantId == currentTenant.TenantId)
            .Include(x => x.TagAssignments).Include(x => x.TagExclusions)
            .SingleOrDefaultAsync(cancellationToken);

        if (transaction is null)
        {
            return TypedResults.NotFound();
        }

        if (request.TagIds is null)
        {
            return TypedResults.BadRequest("Tag IDs are required.");
        }
        var tagIds = request.TagIds.Distinct().ToList();
        var manualIds = (request.ManualTagIds ?? []).ToHashSet();
        if (!manualIds.IsSubsetOf(tagIds))
        {
            return TypedResults.BadRequest("Manual tag IDs must be selected tags.");
        }
        var tags = await dbContext.TransactionTags
            .Where(x => x.TenantId == currentTenant.TenantId && tagIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        if (tags.Count != tagIds.Count)
        {
            return TypedResults.BadRequest("One or more tags do not exist.");
        }

        tagService.SetTags(transaction, tagIds.ToHashSet(), manualIds);

        await projectionInvalidator.TransactionChanged(
            currentTenant.TenantId,
            transaction.AccountId,
            transaction.PostedAt,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        return TypedResults.Ok(await ReadTransactionTags(currentTenant.TenantId, transactionId, dbContext, cancellationToken));
    }

    private static async Task<Results<Ok<IReadOnlyList<TransactionTagResponse>>, NotFound>> RestoreAutomaticTags(
        Guid transactionId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
        TransactionTagService tagService,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var databaseTransaction = await tagService.BeginMutation(currentTenant.TenantId, cancellationToken);
        var transaction = await dbContext.Transactions
            .Where(x => x.TenantId == currentTenant.TenantId && x.Id == transactionId)
            .Include(x => x.TagAssignments).Include(x => x.TagExclusions)
            .SingleOrDefaultAsync(cancellationToken);
        if (transaction is null)
        {
            return TypedResults.NotFound();
        }
        tagService.ClearExclusions(transaction);
        tagService.Reconcile(transaction, await tagService.GetRules(currentTenant.TenantId, cancellationToken));
        await projectionInvalidator.TransactionChanged(currentTenant.TenantId, transaction.AccountId, transaction.PostedAt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        return TypedResults.Ok(await ReadTransactionTags(currentTenant.TenantId, transactionId, dbContext, cancellationToken));
    }

    private static async Task<IReadOnlyList<TransactionTagResponse>> ReadTransactionTags(
        Guid tenantId, Guid transactionId, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        return await dbContext.TransactionTagAssignments.AsNoTracking()
            .Where(x => x.TransactionId == transactionId && x.Transaction != null && x.Transaction.TenantId == tenantId
                && x.Tag != null && x.Tag.TenantId == tenantId)
            .OrderBy(x => x.Tag!.Name)
            .Select(x => new TransactionTagResponse(x.TagId, x.Tag!.Name, x.Tag.Color,
                x.Source, x.MerchantRuleId, x.MerchantRule == null ? null : x.MerchantRule.MerchantName))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Ok<IReadOnlyList<MerchantTagRuleResponse>>> GetMerchantRules(
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var rules = await dbContext.MerchantTagRules
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId)
            .OrderBy(x => x.MerchantName)
            .Select(x => new MerchantTagRuleResponse(
                x.Id,
                x.MerchantName,
                new TransactionTagResponse(x.TagId, x.Tag == null ? "" : x.Tag.Name, x.Tag == null ? "#64748b" : x.Tag.Color),
                x.MerchantKey, x.MerchantKey != MerchantTagMatcher.Normalize(x.MerchantName)))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<MerchantTagRuleResponse>>(rules);
    }

    private static async Task<Results<Created<MerchantTagRuleResponse>, BadRequest<string>, NotFound, Conflict<string>>> CreateMerchantRule(
        CreateMerchantTagRuleRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
        TransactionTagService tagService,
        CancellationToken cancellationToken)
    {
        var merchantName = request.MerchantName?.Trim();
        var merchantKey = MerchantTagMatcher.Normalize(merchantName);
        if (string.IsNullOrEmpty(merchantKey) || merchantName!.Length > 256)
        {
            return TypedResults.BadRequest("Merchant name must contain letters or numbers and be at most 256 characters.");
        }

        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var databaseTransaction = await tagService.BeginMutation(currentTenant.TenantId, cancellationToken);
        var tag = await dbContext.TransactionTags
            .SingleOrDefaultAsync(x => x.Id == request.TagId && x.TenantId == currentTenant.TenantId, cancellationToken);

        if (tag is null)
        {
            return TypedResults.NotFound();
        }

        var rules = await tagService.GetRules(currentTenant.TenantId, cancellationToken);
        var exists = rules.Any(x => x.MerchantKey == merchantKey && x.TagId == tag.Id);

        if (exists)
        {
            return TypedResults.Conflict("This merchant rule already exists.");
        }

        var rule = new MerchantTagRule
        {
            TenantId = currentTenant.TenantId,
            MerchantName = merchantName,
            MerchantKey = merchantKey,
            TagId = tag.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.MerchantTagRules.Add(rule);
        await tagService.ReconcileTenant(currentTenant.TenantId, cancellationToken);
        await projectionInvalidator.TenantProjectionDataChanged(currentTenant.TenantId, "merchant tag rule applied", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }

        var response = new MerchantTagRuleResponse(rule.Id, rule.MerchantName, new TransactionTagResponse(tag.Id, tag.Name, tag.Color));
        return TypedResults.Created($"/api/merchant-tags/{rule.Id}", response);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteMerchantRule(
        Guid ruleId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
        TransactionTagService tagService,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var databaseTransaction = await tagService.BeginMutation(currentTenant.TenantId, cancellationToken);
        var rule = await dbContext.MerchantTagRules
            .SingleOrDefaultAsync(x => x.Id == ruleId && x.TenantId == currentTenant.TenantId, cancellationToken);

        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.MerchantTagRules.Remove(rule);
        await tagService.ReconcileTenant(currentTenant.TenantId, cancellationToken);
        await projectionInvalidator.TenantProjectionDataChanged(currentTenant.TenantId, "merchant tag rule deleted", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MerchantTagRuleResponse>, BadRequest<string>, NotFound, Conflict<string>>> UpdateMerchantRule(
        Guid ruleId,
        CreateMerchantTagRuleRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
        TransactionTagService tagService,
        CancellationToken cancellationToken)
    {
        var merchantName = request.MerchantName?.Trim();
        var merchantKey = MerchantTagMatcher.Normalize(merchantName);
        if (string.IsNullOrEmpty(merchantKey) || merchantName!.Length > 256)
        {
            return TypedResults.BadRequest("Merchant name must contain letters or numbers and be at most 256 characters.");
        }
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        await using var databaseTransaction = await tagService.BeginMutation(currentTenant.TenantId, cancellationToken);
        var rules = await tagService.GetRules(currentTenant.TenantId, cancellationToken);
        var rule = rules.SingleOrDefault(x => x.Id == ruleId);
        var tag = await dbContext.TransactionTags.SingleOrDefaultAsync(x => x.TenantId == currentTenant.TenantId && x.Id == request.TagId, cancellationToken);
        if (rule is null || tag is null)
        {
            return TypedResults.NotFound();
        }
        if (rules.Any(x => x.Id != ruleId && x.MerchantKey == merchantKey && x.TagId == tag.Id))
        {
            return TypedResults.Conflict("This merchant rule already exists.");
        }
        rule.MerchantName = merchantName!;
        rule.MerchantKey = merchantKey;
        rule.TagId = tag.Id;
        rule.Tag = tag;
        await tagService.ReconcileTenant(currentTenant.TenantId, cancellationToken);
        await projectionInvalidator.TenantProjectionDataChanged(currentTenant.TenantId, "merchant tag rule updated", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        return TypedResults.Ok(new MerchantTagRuleResponse(rule.Id, rule.MerchantName, new TransactionTagResponse(tag.Id, tag.Name, tag.Color)));
    }

    private static string GetCategory(string? primaryCategory, string? secondaryCategory)
    {
        if (!string.IsNullOrWhiteSpace(secondaryCategory))
        {
            return secondaryCategory;
        }

        return string.IsNullOrWhiteSpace(primaryCategory) ? "Uncategorised" : primaryCategory;
    }

    private static string NormalizeColor(string? color)
    {
        if (color is not null && HexColorRegex().IsMatch(color))
        {
            return color.ToLowerInvariant();
        }

        return "#64748b";
    }

    private static long ToMinorUnits(decimal amount)
    {
        return (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
    }

    private static string GetPostedDate(DateTimeOffset postedAt)
    {
        return postedAt.UtcDateTime.ToString("yyyy-MM-dd");
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

    private sealed record TransactionResponse(
        Guid Id,
        Guid AccountId,
        string AccountDisplayName,
        string PostedDate,
        string Description,
        string? MerchantName,
        string Category,
        long AmountMinorUnits,
        string Currency,
        bool IsInternalTransfer,
        IReadOnlyList<TransactionTagResponse> Tags,
        IReadOnlyList<Guid> AutomaticTagExclusions);

    private sealed record TransactionPageResponse(
        IReadOnlyList<TransactionResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record TransactionTagResponse(Guid Id, string Name, string Color,
        string? Source = null, Guid? MerchantRuleId = null, string? MerchantRuleName = null);

    private sealed record CreateTransactionTagRequest(string Name, string? Color);

    private sealed record SetTransactionTagsRequest(IReadOnlyList<Guid>? TagIds, IReadOnlyList<Guid>? ManualTagIds = null);

    private sealed record MerchantTagRuleResponse(Guid Id, string MerchantName, TransactionTagResponse Tag,
        string? MatchingWords = null, bool UsesLegacyMatchingWords = false);

    private sealed record CreateMerchantTagRuleRequest(string MerchantName, Guid TagId);
}
