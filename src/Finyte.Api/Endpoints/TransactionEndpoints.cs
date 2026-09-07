using System.Text.RegularExpressions;
using Finyte.Api.Tenancy;
using Finyte.Core.Accounts;
using Finyte.Data;
using Finyte.Data.Analytics;
using Finyte.Data.Billing;
using Finyte.Data.Transfers;
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

        var tags = app.MapGroup("/api/tags").RequireAuthorization();
        tags.MapGet("/", GetTags).WithName("GetTransactionTags");
        tags.MapPost("/", CreateTag).WithName("CreateTransactionTag");
        tags.MapDelete("/{tagId:guid}", DeleteTag).WithName("DeleteTransactionTag");

        var merchantTags = app.MapGroup("/api/merchant-tags").RequireAuthorization();
        merchantTags.MapGet("/", GetMerchantRules).WithName("GetMerchantTagRules");
        merchantTags.MapPost("/", CreateMerchantRule).WithName("CreateMerchantTagRule");
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
            Sort = sort ?? "-date"
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
        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await filters.Order(query)
            .Skip((currentPage - 1) * take)
            .Take(take)
            .Select(x => new TransactionResponse(
                x.Id,
                x.AccountId,
                x.Account == null ? "Account" : x.Account.Name,
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
                    .Select(y => new TransactionTagResponse(y.TagId, y.Tag == null ? "" : y.Tag.Name, y.Tag == null ? "#64748b" : y.Tag.Color))
                    .ToList()))
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
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var tag = await dbContext.TransactionTags
            .SingleOrDefaultAsync(x => x.Id == tagId && x.TenantId == currentTenant.TenantId, cancellationToken);

        if (tag is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.TransactionTags.Remove(tag);
        await projectionInvalidator.TenantProjectionDataChanged(currentTenant.TenantId, "transaction tag deleted", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<TransactionTagResponse>>, NotFound, BadRequest<string>>> SetTransactionTags(
        Guid transactionId,
        SetTransactionTagsRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var transaction = await dbContext.Transactions
            .Where(x => x.Id == transactionId && x.TenantId == currentTenant.TenantId)
            .Select(x => new { x.AccountId, x.PostedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (transaction is null)
        {
            return TypedResults.NotFound();
        }

        var tagIds = request.TagIds.Distinct().ToList();
        var tags = await dbContext.TransactionTags
            .Where(x => x.TenantId == currentTenant.TenantId && tagIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        if (tags.Count != tagIds.Count)
        {
            return TypedResults.BadRequest("One or more tags do not exist.");
        }

        var existingAssignments = await dbContext.TransactionTagAssignments
            .Where(x => x.TransactionId == transactionId)
            .ToListAsync(cancellationToken);
        var existingTagIds = existingAssignments.Select(x => x.TagId).ToHashSet();
        var nextTagIds = tagIds.ToHashSet();

        dbContext.TransactionTagAssignments.RemoveRange(existingAssignments.Where(x => !nextTagIds.Contains(x.TagId)));

        foreach (var tagId in nextTagIds.Where(x => !existingTagIds.Contains(x)))
        {
            dbContext.TransactionTagAssignments.Add(new TransactionTagAssignment
            {
                TransactionId = transactionId,
                TagId = tagId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await projectionInvalidator.TransactionChanged(
            currentTenant.TenantId,
            transaction.AccountId,
            transaction.PostedAt,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<TransactionTagResponse>>(tags.Select(x => new TransactionTagResponse(x.Id, x.Name, x.Color)).ToList());
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
                new TransactionTagResponse(x.TagId, x.Tag == null ? "" : x.Tag.Name, x.Tag == null ? "#64748b" : x.Tag.Color)))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<MerchantTagRuleResponse>>(rules);
    }

    private static async Task<Results<Created<MerchantTagRuleResponse>, BadRequest<string>, NotFound, Conflict<string>>> CreateMerchantRule(
        CreateMerchantTagRuleRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MerchantName))
        {
            return TypedResults.BadRequest("Merchant name is required.");
        }

        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var tag = await dbContext.TransactionTags
            .SingleOrDefaultAsync(x => x.Id == request.TagId && x.TenantId == currentTenant.TenantId, cancellationToken);

        if (tag is null)
        {
            return TypedResults.NotFound();
        }

        var merchantName = request.MerchantName.Trim();
        var merchantKey = GetMerchantKey(merchantName);
        var exists = await dbContext.MerchantTagRules
            .AnyAsync(x => x.TenantId == currentTenant.TenantId && x.MerchantKey == merchantKey && x.TagId == tag.Id, cancellationToken);

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
        await ApplyMerchantRule(currentTenant.TenantId, merchantKey, tag.Id, dbContext, cancellationToken);
        await projectionInvalidator.TenantProjectionDataChanged(currentTenant.TenantId, "merchant tag rule applied", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new MerchantTagRuleResponse(rule.Id, rule.MerchantName, new TransactionTagResponse(tag.Id, tag.Name, tag.Color));
        return TypedResults.Created($"/api/merchant-tags/{rule.Id}", response);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteMerchantRule(
        Guid ruleId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var rule = await dbContext.MerchantTagRules
            .SingleOrDefaultAsync(x => x.Id == ruleId && x.TenantId == currentTenant.TenantId, cancellationToken);

        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.MerchantTagRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task ApplyMerchantRule(
        Guid tenantId,
        string merchantKey,
        Guid tagId,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var transactionIds = await dbContext.Transactions
            .Where(x => x.TenantId == tenantId)
            .Select(x => new { x.Id, x.MerchantName, x.Description })
            .ToListAsync(cancellationToken);
        var matchingIds = transactionIds
            .Where(x => MatchesMerchantRule(GetTransactionMerchantKey(x.MerchantName, x.Description), merchantKey))
            .Select(x => x.Id)
            .ToList();
        var existingIds = await dbContext.TransactionTagAssignments
            .Where(x => x.TagId == tagId && matchingIds.Contains(x.TransactionId))
            .Select(x => x.TransactionId)
            .ToListAsync(cancellationToken);
        var existingIdSet = existingIds.ToHashSet();

        foreach (var transactionId in matchingIds.Where(x => !existingIdSet.Contains(x)))
        {
            dbContext.TransactionTagAssignments.Add(new TransactionTagAssignment
            {
                TransactionId = transactionId,
                TagId = tagId,
                CreatedAt = now
            });
        }
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

    private static string GetMerchantKey(string merchantName)
    {
        var ignoredTokens = new HashSet<string>(["au", "aus", "vi", "pty", "ltd", "limited", "australia", "melbourne", "sydney", "brisbane", "card", "com"]);
        return string.Join(
            " ",
            NonAlphaNumericRegex()
                .Replace(merchantName.Trim().ToLowerInvariant(), " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !ignoredTokens.Contains(x)));
    }

    private static string GetTransactionMerchantKey(string? merchantName, string? description)
    {
        var value = string.IsNullOrWhiteSpace(merchantName) ? description : merchantName;
        return string.IsNullOrWhiteSpace(value) ? "" : GetMerchantKey(value);
    }

    private static bool MatchesMerchantRule(string transactionMerchantKey, string ruleMerchantKey)
    {
        return transactionMerchantKey == ruleMerchantKey
            || transactionMerchantKey.StartsWith($"{ruleMerchantKey} ", StringComparison.Ordinal);
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

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphaNumericRegex();

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
        IReadOnlyList<TransactionTagResponse> Tags);

    private sealed record TransactionPageResponse(
        IReadOnlyList<TransactionResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record TransactionTagResponse(Guid Id, string Name, string Color);

    private sealed record CreateTransactionTagRequest(string Name, string? Color);

    private sealed record SetTransactionTagsRequest(IReadOnlyList<Guid> TagIds);

    private sealed record MerchantTagRuleResponse(Guid Id, string MerchantName, TransactionTagResponse Tag);

    private sealed record CreateMerchantTagRuleRequest(string MerchantName, Guid TagId);
}
