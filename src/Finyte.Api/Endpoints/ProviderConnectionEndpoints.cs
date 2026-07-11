using System.Net.Mail;
using System.Text.RegularExpressions;
using Finyte.Api.Tenancy;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.Billing;
using Finyte.Data.ProviderSync;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static partial class ProviderConnectionEndpoints
{
    public static IEndpointRouteBuilder MapProviderConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/provider-connections").RequireAuthorization();

        group.MapGet("/", GetConnections).WithName("GetProviderConnections");
        group.MapPost("/fiskil/session", StartFiskilSession).WithName("StartFiskilSession");
        group.MapPost("/fiskil/complete", CompleteFiskilSession).WithName("CompleteFiskilSession");
        group.MapDelete("/{connectionId:guid}", Disconnect).WithName("DisconnectProviderConnection");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<ProviderConnectionResponse>>> GetConnections(
        TenantResolver tenantResolver,
        HttpContext httpContext,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var currentMemberId = await GetCurrentMemberId(currentTenant, dbContext, cancellationToken);
        var connections = await dbContext.ProviderConnections
            .AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId && x.ConsentId != null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ProviderConnectionResponse(
                x.Id,
                x.Provider,
                x.InstitutionId,
                x.Status,
                x.TenantMemberId == currentMemberId,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<ProviderConnectionResponse>>(connections);
    }

    private static async Task<Results<Ok<FiskilSessionResponse>, BadRequest<string>, ProblemHttpResult>> StartFiskilSession(
        StartFiskilSessionRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        IFiskilLinkClient fiskilLinkClient,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateContact(request);
        if (validationError is not null)
        {
            return TypedResults.BadRequest(validationError);
        }

        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return TypedResults.Problem("An active subscription is required before connecting providers.", statusCode: StatusCodes.Status402PaymentRequired);
        }

        var currentMemberId = await GetCurrentMemberId(currentTenant, dbContext, cancellationToken);
        var endUserId = await dbContext.ProviderConnections
            .Where(x => x.TenantMemberId == currentMemberId && x.Provider == ProviderSyncProvider.Fiskil)
            .Select(x => x.EndUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(endUserId))
        {
            endUserId = await fiskilLinkClient.CreateEndUser(request.Name.Trim(), request.Email.Trim(), request.Phone.Trim(), cancellationToken);
            dbContext.ProviderConnections.Add(new ProviderConnection
            {
                TenantId = currentTenant.TenantId,
                TenantMemberId = currentMemberId,
                Provider = ProviderSyncProvider.Fiskil,
                EndUserId = endUserId,
                Status = ProviderConnectionStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        var fiskilSession = await fiskilLinkClient.CreateAuthSession(endUserId, cancellationToken);
        dbContext.ProviderAuthSessions.Add(new ProviderAuthSession
        {
            TenantId = currentTenant.TenantId,
            TenantMemberId = currentMemberId,
            Provider = ProviderSyncProvider.Fiskil,
            EndUserId = endUserId,
            SessionId = fiskilSession.SessionId,
            ExpiresAt = fiskilSession.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new FiskilSessionResponse(fiskilSession.SessionId, fiskilSession.ExpiresAt));
    }

    private static async Task<Results<Ok<ProviderConnectionResponse>, BadRequest<string>, NotFound>> CompleteFiskilSession(
        CompleteFiskilSessionRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IFiskilLinkClient fiskilLinkClient,
        IProviderSyncQueue providerSyncQueue,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.ConsentId))
        {
            return TypedResults.BadRequest("Session id and consent id are required.");
        }

        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var currentMemberId = await GetCurrentMemberId(currentTenant, dbContext, cancellationToken);
        var authSession = await dbContext.ProviderAuthSessions
            .SingleOrDefaultAsync(x => x.Provider == ProviderSyncProvider.Fiskil && x.SessionId == request.SessionId && x.TenantMemberId == currentMemberId, cancellationToken);

        if (authSession is null)
        {
            return TypedResults.NotFound();
        }

        if (authSession.CompletedAt is not null || authSession.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return TypedResults.BadRequest("The Fiskil auth session has already completed or expired.");
        }

        var consentId = request.ConsentId.Trim();
        var fiskilConsent = await fiskilLinkClient.GetActiveConsent(authSession.EndUserId, consentId, cancellationToken);
        if (fiskilConsent is null)
        {
            return TypedResults.BadRequest("Fiskil did not confirm an active consent for this session.");
        }

        var connection = await dbContext.ProviderConnections
            .SingleOrDefaultAsync(x => x.TenantId == currentTenant.TenantId && x.TenantMemberId == currentMemberId && x.Provider == ProviderSyncProvider.Fiskil && x.ConsentId == consentId, cancellationToken);
        connection ??= await dbContext.ProviderConnections
            .Where(x => x.TenantMemberId == currentMemberId && x.Provider == ProviderSyncProvider.Fiskil && x.EndUserId == authSession.EndUserId && x.ConsentId == null)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (connection is null)
        {
            connection = new ProviderConnection
            {
                TenantId = currentTenant.TenantId,
                TenantMemberId = currentMemberId,
                Provider = ProviderSyncProvider.Fiskil,
                EndUserId = authSession.EndUserId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.ProviderConnections.Add(connection);
        }

        connection.ConsentId = consentId;
        connection.InstitutionId = fiskilConsent.InstitutionId;
        connection.Status = ProviderConnectionStatus.Active;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        authSession.CompletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await providerSyncQueue.EnqueueInitialSync(connection, cancellationToken);

        return TypedResults.Ok(ToResponse(connection, currentMemberId));
    }

    private static async Task<IResult> Disconnect(
        Guid connectionId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IFiskilLinkClient fiskilLinkClient,
        FinyteDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var currentMemberId = await GetCurrentMemberId(currentTenant, dbContext, cancellationToken);
        var connection = await dbContext.ProviderConnections
            .SingleOrDefaultAsync(x => x.Id == connectionId
                && x.TenantId == currentTenant.TenantId
                && x.TenantMemberId == currentMemberId,
                cancellationToken);

        if (connection is null)
        {
            return TypedResults.NotFound();
        }

        if (connection.Status == ProviderConnectionStatus.Revoked)
        {
            return TypedResults.NoContent();
        }

        if (!string.IsNullOrWhiteSpace(connection.ConsentId))
        {
            await fiskilLinkClient.RevokeConsent(connection.ConsentId, cancellationToken);
        }

        connection.Status = ProviderConnectionStatus.Revoked;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        var queuedSyncRuns = await dbContext.ProviderSyncRuns
            .Where(x => x.TenantId == connection.TenantId
                && x.ConsentId == connection.ConsentId
                && x.Status == ProviderSyncStatus.Queued)
            .ToListAsync(cancellationToken);
        foreach (var syncRun in queuedSyncRuns)
        {
            syncRun.Status = ProviderSyncStatus.Cancelled;
            syncRun.CompletedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Guid> GetCurrentMemberId(CurrentTenant currentTenant, FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        return await dbContext.TenantMembers
            .Where(x => x.TenantId == currentTenant.TenantId && x.UserId == currentTenant.UserId && x.RemovedAt == null)
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);
    }

    private static string? ValidateContact(StartFiskilSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (!MailAddress.TryCreate(request.Email, out _))
        {
            return "A valid email address is required.";
        }

        return E164PhoneRegex().IsMatch(request.Phone?.Trim() ?? "") ? null : "Phone must use E.164 format, for example +61412345678.";
    }

    private static ProviderConnectionResponse ToResponse(ProviderConnection connection, Guid currentMemberId)
    {
        return new ProviderConnectionResponse(
            connection.Id,
            connection.Provider,
            connection.InstitutionId,
            connection.Status,
            connection.TenantMemberId == currentMemberId,
            connection.CreatedAt,
            connection.UpdatedAt);
    }

    [GeneratedRegex("^\\+[1-9]\\d{7,14}$")]
    private static partial Regex E164PhoneRegex();

    private sealed record StartFiskilSessionRequest(string Name, string Email, string Phone);

    private sealed record CompleteFiskilSessionRequest(string SessionId, string ConsentId);

    private sealed record FiskilSessionResponse(string SessionId, DateTimeOffset ExpiresAt);

    private sealed record ProviderConnectionResponse(
        Guid Id,
        string Provider,
        string? InstitutionId,
        string Status,
        bool IsOwnedByCurrentMember,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
