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

        return app;
    }

    private static async Task<Ok<IReadOnlyList<AccountResponse>>> GetAccounts(
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId)
            .OrderBy(x => x.Name)
            .Select(x => new AccountResponse(x.Id, x.Name, x.CurrentBalance, x.AvailableBalance, x.Currency, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<AccountResponse>>(accounts);
    }

    private static async Task<Results<Created<AccountResponse>, BadRequest<string>, ProblemHttpResult>> CreateAccount(
        CreateAccountRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        FinyteDbContext dbContext,
        IProjectionInvalidator projectionInvalidator,
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

        var account = new Account
        {
            TenantId = currentTenant.TenantId,
            Name = name,
            CurrentBalance = request.CurrentBalance,
            AvailableBalance = request.AvailableBalance,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "AUD" : request.Currency.Trim().ToUpperInvariant(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Accounts.Add(account);
        await projectionInvalidator.TenantProjectionDataChanged(currentTenant.TenantId, "manual account created", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new AccountResponse(account.Id, account.Name, account.CurrentBalance, account.AvailableBalance, account.Currency, account.CreatedAt);

        return TypedResults.Created($"/api/accounts/{account.Id}", response);
    }

    private sealed record CreateAccountRequest(string Name, decimal CurrentBalance, decimal? AvailableBalance, string? Currency);

    private sealed record AccountResponse(Guid Id, string Name, decimal CurrentBalance, decimal? AvailableBalance, string Currency, DateTimeOffset CreatedAt);
}
