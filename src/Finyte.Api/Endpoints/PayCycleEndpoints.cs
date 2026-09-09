using Finyte.Api.Tenancy;
using Finyte.Core.PayCycles;
using Finyte.Data;
using Finyte.Data.Billing;
using Finyte.Data.PayCycles;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class PayCycleEndpoints
{
    public static IEndpointRouteBuilder MapPayCycleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pay-cycles").RequireAuthorization();
        group.MapGet("/", List).WithName("GetPayCycles");
        group.MapPost("/", Create).WithName("CreatePayCycle");
        group.MapPut("/{profileId:guid}", Update).WithName("UpdatePayCycle");
        group.MapDelete("/{profileId:guid}", Delete).WithName("DeletePayCycle");
        group.MapGet("/{profileId:guid}/breakdown", GetBreakdown).WithName("GetPayCycleBreakdown");
        return app;
    }

    private static async Task<IResult> List(TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        var profiles = await dbContext.PayCycleProfiles.AsNoTracking().Where(x => x.TenantId == tenant.TenantId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        return Results.Ok(profiles.Select(x => PayCycleQueries.ToResponse(x)).ToList());
    }

    private static async Task<IResult> Create(SavePayCycleRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        var error = await Validate(request, tenant.TenantId, dbContext, cancellationToken);
        if (error is not null)
        {
            return Results.BadRequest(error);
        }
        var profile = new PayCycleProfile { TenantId = tenant.TenantId, Name = "", Frequency = "", Currency = "", CreatedAt = timeProvider.GetUtcNow() };
        Apply(profile, request, timeProvider.GetUtcNow());
        dbContext.PayCycleProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/pay-cycles/{profile.Id}/breakdown", PayCycleQueries.ToResponse(profile));
    }

    private static async Task<IResult> Update(Guid profileId, SavePayCycleRequest request, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        var profile = await dbContext.PayCycleProfiles.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == profileId, cancellationToken);
        if (profile is null)
        {
            return Results.NotFound();
        }
        if (request.ExpectedVersion is null or < 0)
        {
            return Results.BadRequest("The current profile version is required.");
        }
        if (profile.Version != request.ExpectedVersion)
        {
            return StaleProfile();
        }
        var error = await Validate(request, tenant.TenantId, dbContext, cancellationToken);
        if (error is not null)
        {
            return Results.BadRequest(error);
        }
        Apply(profile, request, timeProvider.GetUtcNow());
        profile.Version++;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StaleProfile();
        }
        return Results.Ok(PayCycleQueries.ToResponse(profile));
    }

    private static async Task<IResult> Delete(Guid profileId, int? expectedVersion, TenantResolver tenantResolver, HttpContext httpContext,
        IBillingAccess billingAccess, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        var profile = await dbContext.PayCycleProfiles.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == profileId, cancellationToken);
        if (profile is null)
        {
            return Results.NotFound();
        }
        if (expectedVersion is null or < 0)
        {
            return Results.BadRequest("The current profile version is required.");
        }
        if (profile.Version != expectedVersion)
        {
            return StaleProfile();
        }
        dbContext.PayCycleProfiles.Remove(profile);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StaleProfile();
        }
        return Results.NoContent();
    }

    private static async Task<IResult> GetBreakdown(Guid profileId, DateOnly? date, int? page, int? pageSize, string? kind,
        TenantResolver tenantResolver, HttpContext httpContext, IBillingAccess billingAccess,
        PayCycleQueries queries, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return SubscriptionRequired();
        }
        var currentPage = page ?? 1;
        var size = pageSize ?? 25;
        if (date.HasValue && !PayCycleCalendar.ValidDate(date.Value) || currentPage < 1 || size is < 1 or > 100
            || (long)(currentPage - 1) * size > int.MaxValue || kind is not null && !PayCycleQueries.Kinds.Contains(kind))
        {
            return Results.BadRequest("Use a date between 1900-01-01 and 9998-12-31, positive page, page size 1–100 and a supported transaction kind.");
        }
        var response = await queries.GetBreakdown(tenant.TenantId, profileId, date, currentPage, size, kind, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<string?> Validate(SavePayCycleRequest request, Guid tenantId, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120
            || request.Frequency is null || !PayCycleCalendar.Frequencies.Contains(request.Frequency)
            || request.AnchorDate is null || !PayCycleCalendar.ValidDate(request.AnchorDate.Value)
            || request.Currency is null || request.Currency.Trim().Length != 3 || !request.Currency.Trim().All(x => char.IsAsciiLetter(x)))
        {
            return "Provide a name up to 120 characters, weekly/fortnightly/monthly frequency, anchor date between 1900-01-01 and 9998-12-31, and a three-letter currency.";
        }
        if (request.ExpectedIncome is { } expected && (expected < 0 || expected > 9999999999999999.99m || decimal.Round(expected, 2) != expected))
        {
            return "Expected income must be non-negative, with at most two decimal places and 16 whole digits, or null.";
        }
        if (request.AccountIds is null || request.AccountIds.Length is < 1 or > 100 || request.SavingsAccountIds?.Length > 100)
        {
            return "Select 1–100 accounts for the breakdown and at most 100 savings destinations.";
        }
        var savingsIds = request.SavingsAccountIds ?? [];
        var ids = request.AccountIds.Concat(savingsIds).ToArray();
        if (ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Length)
        {
            return "Account selections must be unique. Savings destinations must be outside the breakdown accounts.";
        }
        var currency = request.Currency.Trim().ToUpperInvariant();
        var count = await dbContext.Accounts.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id) && x.Currency == currency, cancellationToken);
        return count == ids.Length ? null : "All selected accounts must belong to this family and use the profile currency.";
    }

    private static void Apply(PayCycleProfile profile, SavePayCycleRequest request, DateTimeOffset now)
    {
        profile.Name = request.Name!.Trim();
        profile.Frequency = request.Frequency!;
        profile.AnchorDate = request.AnchorDate!.Value;
        profile.Currency = request.Currency!.Trim().ToUpperInvariant();
        profile.ExpectedIncome = request.ExpectedIncome;
        profile.AccountIds = request.AccountIds!;
        profile.SavingsAccountIds = request.SavingsAccountIds ?? [];
        profile.UpdatedAt = now;
    }

    private static IResult SubscriptionRequired() => Results.Problem("An active subscription is required for pay-cycle breakdowns.", statusCode: StatusCodes.Status402PaymentRequired);
    private static IResult StaleProfile() => Results.Conflict("This pay-cycle profile changed. Reload it before saving or deleting.");
    private sealed record SavePayCycleRequest(string? Name, string? Frequency, DateOnly? AnchorDate, string? Currency,
        decimal? ExpectedIncome, Guid[]? AccountIds, Guid[]? SavingsAccountIds, int? ExpectedVersion);
}
