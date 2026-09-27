using Finyte.Api.Tenancy;
using Finyte.Data.Billing;
using Finyte.Data.Tagging;

namespace Finyte.Api.Endpoints;

public static class TagSuggestionEndpoints
{
    private static object TenantKey { get; } = new();

    public static IEndpointRouteBuilder MapTagSuggestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tag-suggestions").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var tenant = await httpContext.RequestServices.GetRequiredService<TenantResolver>().Resolve(httpContext.User, httpContext.RequestAborted);
            if (!await httpContext.RequestServices.GetRequiredService<IBillingAccess>().HasAccess(tenant.TenantId, httpContext.RequestAborted))
            {
                return Results.Problem("An active subscription is required for tag suggestions.", statusCode: 402);
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
            catch (ArgumentException exception)
            {
                return Results.BadRequest(exception.Message);
            }
        });
        group.MapGet("/", Get).WithName("GetTagSuggestions");
        group.MapPost("/accept", Accept).WithName("AcceptTagSuggestions");
        return app;
    }

    private static async Task<IResult> Get(DateOnly? from, DateOnly? to, HttpContext httpContext, TagSuggestionService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.Get(Tenant(httpContext).TenantId, from, to, cancellationToken));

    private static async Task<IResult> Accept(AcceptTagSuggestionsRequest request, HttpContext httpContext, TagSuggestionService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.Accept(Tenant(httpContext).TenantId, request, cancellationToken));

    private static CurrentTenant Tenant(HttpContext httpContext) => (CurrentTenant)httpContext.Items[TenantKey]!;
}
