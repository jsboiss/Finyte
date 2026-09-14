using Finyte.Api.Tenancy;
using Finyte.Core.Accounts;
using Finyte.Data;
using Finyte.Data.Analytics;
using Finyte.Data.Billing;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class BankingAccountEndpoints
{
    public static IEndpointRouteBuilder MapBankingAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").RequireAuthorization();
        group.MapGet("/", GetAccounts).WithName("GetAccounts");
        group.MapPost("/", CreateAccount).WithName("CreateAccount");
        group.MapPut("/{accountId:guid}/preferences", UpdatePreferences).WithName("UpdateAccountPreferences");
        return app;
    }

    private static async Task<Ok<IReadOnlyList<AccountResponse>>> GetAccounts(
        TenantResolver tenantResolver, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var accounts = await dbContext.Accounts.AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId)
            .OrderBy(x => x.CustomName ?? x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<AccountResponse>>(accounts.Select(x => ToResponse(x)).ToList());
    }

    private static async Task<Results<Created<AccountResponse>, BadRequest<string>, ProblemHttpResult>> CreateAccount(
        CreateAccountRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, IProjectionInvalidator projectionInvalidator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest("Account name is required.");
        }
        var name = request.Name.Trim();
        if (name.Length > 120 || (!string.IsNullOrWhiteSpace(request.Currency)
            && (request.Currency.Trim().Length != 3 || !request.Currency.Trim().All(x => char.IsAsciiLetter(x)))))
        {
            return TypedResults.BadRequest("Use an account name of up to 120 characters and a three-letter currency code.");
        }
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return TypedResults.Problem("An active subscription is required before creating accounts.", statusCode: StatusCodes.Status402PaymentRequired);
        }
        var now = DateTimeOffset.UtcNow;
        var account = new Account
        {
            TenantId = currentTenant.TenantId, Name = name,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "AUD" : request.Currency.Trim().ToUpperInvariant(),
            CreatedAt = now
        };
        dbContext.Accounts.Add(account);
        await projectionInvalidator.TenantProjectionDataChanged(currentTenant.TenantId, "manual account created", cancellationToken);
        return TypedResults.Created($"/api/accounts/{account.Id}", ToResponse(account));
    }

    private static async Task<IResult> UpdatePreferences(
        Guid accountId, UpdateAccountPreferencesRequest request, TenantResolver tenantResolver,
        HttpContext httpContext, FinyteDbContext dbContext, IProjectionInvalidator projectionInvalidator,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(request.CustomName) ? null : request.CustomName.Trim();
        var accountType = request.AccountTypeOverride?.Trim().ToLowerInvariant();
        if (name?.Length > 120 || (accountType is not null && !AccountPreferences.Types.Contains(accountType)))
        {
            return Results.BadRequest("Use a custom name of up to 120 characters and a supported account type, or null to reset a preference.");
        }
        if (request.ExpectedVersion is null or < 0)
        {
            return Results.BadRequest("The current preferences version is required.");
        }
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var account = await dbContext.Accounts.SingleOrDefaultAsync(x => x.TenantId == currentTenant.TenantId && x.Id == accountId, cancellationToken);
        if (account is null)
        {
            return Results.NotFound();
        }
        if (account.PreferencesVersion != request.ExpectedVersion)
        {
            return Results.Conflict("Account preferences changed. Reload the account before saving again.");
        }
        account.CustomName = name;
        account.AccountTypeOverride = accountType;
        account.IncludeInAnalyticsOverride = request.IncludeInAnalyticsOverride;
        account.PreferencesVersion++;
        try
        {
            // Preferences and projection invalidation are saved together by the invalidator.
            await projectionInvalidator.TenantProjectionDataChanged(currentTenant.TenantId, "account preferences changed", cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict("Account data changed. Reload the account before saving again.");
        }
        return Results.Ok(ToResponse(account));
    }

    private static AccountResponse ToResponse(Account account)
    {
        var accountType = AccountPreferences.EffectiveType(account);
        return new AccountResponse(account.Id, AccountPreferences.DisplayName(account), account.CurrentBalance,
            account.AvailableBalance, account.Currency, account.CreatedAt, account.Name, account.CustomName,
            accountType, AccountPreferences.InferredType(account.ProductCategory), account.AccountTypeOverride,
            AccountPreferences.DefaultIncludeInAnalytics(accountType), account.IncludeInAnalyticsOverride,
            AccountPreferences.IncludeInAnalytics(account), AccountPreferences.IsProviderManaged(account),
            account.ProductName, account.ProductCategory, AccountPreferences.HasReportedBalance(account) ? account.BalanceAsOf : null, account.PreferencesVersion, account.ManualBalanceVersion);
    }

    private sealed record CreateAccountRequest(string Name, string? Currency);
    private sealed record UpdateAccountPreferencesRequest(string? CustomName, string? AccountTypeOverride, bool? IncludeInAnalyticsOverride, int? ExpectedVersion);
    private sealed record AccountResponse(Guid Id, string Name, decimal CurrentBalance, decimal? AvailableBalance, string Currency,
        DateTimeOffset CreatedAt, string OriginalName, string? CustomName, string AccountType, string InferredAccountType,
        string? AccountTypeOverride, bool DefaultIncludeInAnalytics, bool? IncludeInAnalyticsOverride, bool IncludeInAnalytics,
        bool IsProviderManaged, string? ProductName, string? ProductCategory, DateTimeOffset? BalanceAsOf, int PreferencesVersion, int ManualBalanceVersion);
}
