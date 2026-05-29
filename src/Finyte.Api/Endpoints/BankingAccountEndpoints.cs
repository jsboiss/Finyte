using Finyte.Core.Accounts;
using Finyte.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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
        ClaimsPrincipal user,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userId = EndpointUser.GetUserId(user);
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Name)
            .Select(x => new AccountResponse(x.Id, x.Name, x.CurrentBalance, x.AvailableBalance, x.Currency, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<AccountResponse>>(accounts);
    }

    private static async Task<Results<Created<AccountResponse>, BadRequest<string>>> CreateAccount(
        CreateAccountRequest request,
        ClaimsPrincipal user,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest("Account name is required.");
        }

        var name = request.Name.Trim();
        var account = new Account
        {
            UserId = EndpointUser.GetUserId(user),
            Name = name,
            CurrentBalance = request.CurrentBalance,
            AvailableBalance = request.AvailableBalance,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "AUD" : request.Currency.Trim().ToUpperInvariant(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new AccountResponse(account.Id, account.Name, account.CurrentBalance, account.AvailableBalance, account.Currency, account.CreatedAt);

        return TypedResults.Created($"/api/accounts/{account.Id}", response);
    }

    private sealed record CreateAccountRequest(string Name, decimal CurrentBalance, decimal? AvailableBalance, string? Currency);

    private sealed record AccountResponse(Guid Id, string Name, decimal CurrentBalance, decimal? AvailableBalance, string Currency, DateTimeOffset CreatedAt);
}
