using Finyte.Api.Tenancy;
using Finyte.Core.Recurring;
using Finyte.Data.Billing;
using Finyte.Data.Recurring;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class RecurringPaymentEndpoints
{
    private static object TenantKey { get; } = new();

    public static IEndpointRouteBuilder MapRecurringPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recurring-payments").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var tenant = await httpContext.RequestServices.GetRequiredService<TenantResolver>().Resolve(httpContext.User, httpContext.RequestAborted);
            if (!await httpContext.RequestServices.GetRequiredService<IBillingAccess>().HasAccess(tenant.TenantId, httpContext.RequestAborted))
            {
                return Results.Problem("An active subscription is required for recurring payments.", statusCode: 402);
            }
            httpContext.Items[TenantKey] = tenant;
            try
            {
                return await next(context);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(exception.Message);
            }
            catch (RecurringConflictException exception)
            {
                return Results.Conflict(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(exception.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict("Another review changed this recurring series. Reload it before trying again.");
            }
            catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                return Results.Conflict("This transaction or occurrence was confirmed by another review. Reload before trying again.");
            }
        });
        group.MapGet("/", List).WithName("GetRecurringPayments");
        group.MapPost("/", Create).WithName("CreateRecurringPayment");
        group.MapGet("/discovery", Discover).WithName("DiscoverRecurringPayments");
        group.MapPost("/discovery/decisions", DiscoveryDecision).WithName("ReviewRecurringDiscovery");
        group.MapPut("/{seriesId:guid}", Update).WithName("UpdateRecurringPayment");
        group.MapGet("/{seriesId:guid}/occurrences", Occurrences).WithName("GetRecurringOccurrences");
        group.MapGet("/{seriesId:guid}/transactions", Candidates).WithName("GetRecurringCandidates");
        group.MapGet("/{seriesId:guid}/history", History).WithName("GetRecurringHistory");
        group.MapPost("/{seriesId:guid}/decisions", Decide).WithName("ReviewRecurringPayment");
        group.MapDelete("/{seriesId:guid}/aliases/{aliasId:guid}", RemoveAlias).WithName("RemoveRecurringAlias");
        return app;
    }

    private static async Task<IResult> List(DateOnly? from, DateOnly? to, HttpContext httpContext, RecurringPaymentService service, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return Results.Ok(await service.List(Tenant(httpContext).TenantId, from ?? today.AddDays(-1096), to ?? today.AddDays(366), cancellationToken));
    }

    private static async Task<IResult> Discover(DateOnly? from, DateOnly? to, int? page, int? pageSize, bool? dismissed, Guid? accountId, string? search, string? cadence, string? sort, HttpContext httpContext,
        RecurringPaymentService service, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return Results.Ok(await service.Discover(Tenant(httpContext).TenantId, from ?? today.AddDays(-1096), to ?? today,
            page ?? 1, pageSize ?? 20, dismissed ?? false, cancellationToken, accountId, search, cadence, sort));
    }

    private static async Task<IResult> DiscoveryDecision(RecurringDiscoveryDecisionRequest request, HttpContext httpContext, RecurringPaymentService service, CancellationToken cancellationToken)
    {
        await service.DiscoveryDecision(Tenant(httpContext).TenantId, request, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> Create(CreateRecurringRequest request, HttpContext httpContext, RecurringPaymentService service, CancellationToken cancellationToken)
    {
        var tenant = Tenant(httpContext);
        var response = await service.Create(tenant.TenantId, tenant.UserId, request, cancellationToken);
        return Results.Created($"/api/recurring-payments/{response.Id}/occurrences", response);
    }

    private static async Task<IResult> Update(Guid seriesId, UpdateRecurringRequest request, HttpContext httpContext, RecurringPaymentService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.Update(Tenant(httpContext).TenantId, seriesId, request, cancellationToken));

    private static async Task<IResult> Occurrences(Guid seriesId, DateOnly? from, DateOnly? to, HttpContext httpContext, RecurringPaymentService service,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return Results.Ok(await service.Occurrences(Tenant(httpContext).TenantId, seriesId, from ?? today.AddDays(-31), to ?? today.AddDays(90), cancellationToken));
    }

    private static async Task<IResult> Candidates(Guid seriesId, DateOnly? from, DateOnly? to, DateOnly? occurrenceDate, int? page, int? pageSize,
        HttpContext httpContext, RecurringPaymentService service, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return Results.Ok(await service.Candidates(Tenant(httpContext).TenantId, seriesId, from ?? today.AddDays(-1096), to ?? today,
            occurrenceDate, page ?? 1, pageSize ?? 25, cancellationToken));
    }

    private static async Task<IResult> History(Guid seriesId, int? page, int? pageSize, HttpContext httpContext, RecurringPaymentService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.History(Tenant(httpContext).TenantId, seriesId, page ?? 1, pageSize ?? 25, cancellationToken));

    private static async Task<IResult> Decide(Guid seriesId, RecurringDecisionRequest request, HttpContext httpContext, RecurringPaymentService service, CancellationToken cancellationToken)
    {
        var tenant = Tenant(httpContext);
        return Results.Ok(await service.Decide(tenant.TenantId, tenant.UserId, seriesId, request, cancellationToken));
    }

    private static async Task<IResult> RemoveAlias(Guid seriesId, Guid aliasId, int? expectedVersion, HttpContext httpContext, RecurringPaymentService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.RemoveAlias(Tenant(httpContext).TenantId, seriesId, aliasId, expectedVersion, cancellationToken));

    private static CurrentTenant Tenant(HttpContext httpContext) => (CurrentTenant)httpContext.Items[TenantKey]!;
}
