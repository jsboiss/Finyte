using Finyte.Api.Tenancy;
using Finyte.Core.Accounts;
using Finyte.Data;
using Finyte.Data.Billing;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class AccountGroupEndpoints
{
    private static object TenantKey { get; } = new();

    public static IEndpointRouteBuilder MapAccountGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account-groups").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var tenant = await httpContext.RequestServices.GetRequiredService<TenantResolver>().Resolve(httpContext.User, httpContext.RequestAborted);
            if (!await httpContext.RequestServices.GetRequiredService<IBillingAccess>().HasAccess(tenant.TenantId, httpContext.RequestAborted))
            {
                return Results.Problem("An active subscription is required for account groups.", statusCode: 402);
            }
            httpContext.Items[TenantKey] = tenant;
            return await next(context);
        });
        group.MapGet("/", List).WithName("GetAccountGroups");
        group.MapPost("/", Create).WithName("CreateAccountGroup");
        group.MapPut("/{groupId:guid}", Update).WithName("UpdateAccountGroup");
        group.MapDelete("/{groupId:guid}", Delete).WithName("DeleteAccountGroup");
        return app;
    }

    private static async Task<IResult> List(HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenantId = Tenant(httpContext).TenantId;
        var groups = await dbContext.AccountGroups.AsNoTracking().Include(x => x.Members)
            .Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return Results.Ok(groups.Select(ToResponse).ToList());
    }

    private static async Task<IResult> Create(AccountGroupRequest request, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenantId = Tenant(httpContext).TenantId;
        if (await Validate(dbContext, tenantId, null, request, cancellationToken) is { } error)
        {
            return error;
        }
        var now = DateTimeOffset.UtcNow;
        var group = new AccountGroup { TenantId = tenantId, Name = request.Name!.Trim(), CreatedAt = now, UpdatedAt = now };
        foreach (var accountId in request.AccountIds!.Distinct())
        {
            group.Members.Add(new AccountGroupMember { GroupId = group.Id, AccountId = accountId });
        }
        dbContext.AccountGroups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/account-groups/{group.Id}", ToResponse(group));
    }

    private static async Task<IResult> Update(Guid groupId, AccountGroupRequest request, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenantId = Tenant(httpContext).TenantId;
        var group = await dbContext.AccountGroups.Include(x => x.Members).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == groupId, cancellationToken);
        if (group is null)
        {
            return Results.NotFound();
        }
        if (await Validate(dbContext, tenantId, groupId, request, cancellationToken) is { } error)
        {
            return error;
        }
        var wanted = request.AccountIds!.Distinct().ToHashSet();
        foreach (var member in group.Members.Where(x => !wanted.Contains(x.AccountId)).ToList())
        {
            group.Members.Remove(member);
        }
        foreach (var accountId in wanted.Where(x => group.Members.All(y => y.AccountId != x)))
        {
            group.Members.Add(new AccountGroupMember { GroupId = group.Id, AccountId = accountId });
        }
        group.Name = request.Name!.Trim();
        group.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(group));
    }

    private static async Task<IResult> Delete(Guid groupId, HttpContext httpContext, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var tenantId = Tenant(httpContext).TenantId;
        var group = await dbContext.AccountGroups.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == groupId, cancellationToken);
        if (group is null)
        {
            return Results.NotFound();
        }
        dbContext.AccountGroups.Remove(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult?> Validate(FinyteDbContext dbContext, Guid tenantId, Guid? groupId, AccountGroupRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var ids = request.AccountIds?.Distinct().ToList() ?? [];
        if (string.IsNullOrEmpty(name) || name.Length > 80)
        {
            return Results.BadRequest("Give the group a name of up to 80 characters.");
        }
        if (ids.Count is 0 or > 100 || ids.Contains(Guid.Empty))
        {
            return Results.BadRequest("Choose between 1 and 100 accounts.");
        }
        if (await dbContext.Accounts.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), cancellationToken) != ids.Count)
        {
            return Results.BadRequest("Choose accounts from this household.");
        }
        var names = await dbContext.AccountGroups.Where(x => x.TenantId == tenantId && x.Id != groupId).Select(x => x.Name).ToListAsync(cancellationToken);
        if (names.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Conflict("A group with this name already exists.");
        }
        return null;
    }

    private static AccountGroupResponse ToResponse(AccountGroup group) =>
        new(group.Id, group.Name, group.Members.Select(x => x.AccountId).OrderBy(x => x).ToList());

    private static CurrentTenant Tenant(HttpContext httpContext) => (CurrentTenant)httpContext.Items[TenantKey]!;

    private sealed record AccountGroupRequest(string? Name, IReadOnlyList<Guid>? AccountIds);
    private sealed record AccountGroupResponse(Guid Id, string Name, IReadOnlyList<Guid> AccountIds);
}
