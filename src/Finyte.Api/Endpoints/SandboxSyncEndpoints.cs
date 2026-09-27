using Finyte.Api.Tenancy;
using Finyte.Api.Development;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class SandboxSyncEndpoints
{
    private static SemaphoreSlim SyncLock { get; } = new(1, 1);

    public static IEndpointRouteBuilder MapSandboxSyncEndpoints(this IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return app;
        }

        var group = app.MapGroup("/api/development/fiskil-sandbox").RequireAuthorization();
        group.MapGet("/", GetStatus);
        group.MapPost("/sync", Sync);
        return app;
    }

    private static bool IsConfigured(IConfiguration configuration)
    {
        return configuration.GetValue<bool>("Fiskil:Sandbox:Enabled")
            && !string.IsNullOrWhiteSpace(configuration["Fiskil:Sandbox:EndUserId"])
            && !string.IsNullOrWhiteSpace(configuration["Fiskil:Sandbox:ConsentId"])
            && !string.IsNullOrWhiteSpace(configuration["Fiskil:ClientId"])
            && !string.IsNullOrWhiteSpace(configuration["Fiskil:ClientSecret"]);
    }

    private static async Task<IResult> GetStatus(
        IConfiguration configuration, TenantResolver tenantResolver, HttpContext httpContext,
        FinyteDbContext dbContext, CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var consentId = configuration["Fiskil:Sandbox:ConsentId"];
        var runs = await dbContext.ProviderSyncRuns.AsNoTracking()
            .Where(x => x.TenantId == currentTenant.TenantId && x.Provider == ProviderSyncProvider.Fiskil && x.ConsentId == consentId)
            .OrderByDescending(x => x.CreatedAt).Take(30)
            .Select(x => new { x.Dataset, x.Status, x.Error, x.CreatedAt, x.CompletedAt })
            .ToListAsync(cancellationToken);
        var latestRuns = runs.GroupBy(x => x.Dataset).Select(x => x.First()).ToList();
        var accountIds = dbContext.Accounts
            .Where(x => x.TenantId == currentTenant.TenantId && x.ConsentId == consentId && x.FiskilAccountId != null)
            .Select(x => x.Id);
        return Results.Ok(new
        {
            Configured = IsConfigured(configuration),
            IsRunning = runs.Any(x => x.Status is ProviderSyncStatus.Queued or ProviderSyncStatus.Running),
            Runs = latestRuns,
            AccountCount = await accountIds.CountAsync(cancellationToken),
            TransactionCount = await dbContext.Transactions.CountAsync(x => x.TenantId == currentTenant.TenantId && accountIds.Contains(x.AccountId), cancellationToken)
        });
    }

    private static async Task<IResult> Sync(
        ResetRequest request,
        IConfiguration configuration, TenantResolver tenantResolver, HttpContext httpContext,
        FinyteDbContext dbContext, IFiskilLinkClient linkClient, IFiskilBankingClient bankingClient,
        IProviderSyncQueue queue, CancellationToken cancellationToken)
    {
        if (!request.ConfirmReset)
        {
            return Results.Problem("Confirm the financial data reset before syncing sandbox data.", statusCode: 400);
        }
        if (!IsConfigured(configuration))
        {
            return Results.Problem("Configure sandbox credentials, EndUserId and ConsentId on the development server first.", statusCode: 400);
        }

        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var memberId = await dbContext.TenantMembers
            .Where(x => x.TenantId == currentTenant.TenantId && x.UserId == currentTenant.UserId && x.RemovedAt == null)
            .Select(x => x.Id).SingleAsync(cancellationToken);
        if (currentTenant.Role != Finyte.Core.Tenancy.TenantRole.Owner)
        {
            return Results.Forbid();
        }
        if (!await SyncLock.WaitAsync(0, cancellationToken))
        {
            return Results.Conflict(new { detail = "A sandbox sync is already being requested." });
        }

        try
        {
            var endUserId = configuration["Fiskil:Sandbox:EndUserId"]!;
            var consentId = configuration["Fiskil:Sandbox:ConsentId"]!;
            var connection = await dbContext.ProviderConnections.SingleOrDefaultAsync(
                x => x.Provider == ProviderSyncProvider.Fiskil && x.ConsentId == consentId, cancellationToken);
            if (connection is not null && (connection.TenantId != currentTenant.TenantId || connection.TenantMemberId != memberId))
            {
                return Results.Conflict(new { detail = "This sandbox consent is already connected to another member. Use that test user or configure a separate sandbox consent." });
            }
            var consent = await linkClient.GetActiveConsent(endUserId, consentId, cancellationToken);
            if (consent?.InstitutionId != "88888")
            {
                return Results.Problem("An active Fiskil sandbox bank consent is required. Complete sandbox consent in Fiskil Console first.", statusCode: 400);
            }
            var accounts = await bankingClient.GetAccounts(endUserId, cancellationToken);
            if (accounts.Count == 0 || accounts.Any(x => x.InstitutionId != "88888" || x.ConsentId != consentId))
            {
                return Results.Problem("Use a dedicated sandbox end user with accounts under this sandbox consent only.", statusCode: 400);
            }

            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
            if (dbContext.Database.IsNpgsql())
            {
                await dbContext.Database.ExecuteSqlRawAsync("LOCK TABLE provider_sync_runs, accounts, transactions IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
            }
            if (await dbContext.ProviderSyncRuns.AnyAsync(x => x.TenantId == currentTenant.TenantId
                && (x.Status == ProviderSyncStatus.Queued || x.Status == ProviderSyncStatus.Running
                    || (x.Status == ProviderSyncStatus.Failed && x.DispatchedAt != null)), cancellationToken))
            {
                return Results.Conflict(new { detail = "Finish or cancel existing provider sync jobs before resetting financial data." });
            }
            if (await dbContext.ProviderConnections.AnyAsync(x => x.TenantId == currentTenant.TenantId
                && x.ConsentId != consentId && x.Status == ProviderConnectionStatus.Active, cancellationToken))
            {
                return Results.Conflict(new { detail = "Disconnect other bank connections before resetting this test household." });
            }

            await SandboxFinancialReset.Clear(dbContext, currentTenant.TenantId, consentId, cancellationToken);

            if (connection is null)
            {
                connection = new ProviderConnection
                {
                    TenantId = currentTenant.TenantId,
                    TenantMemberId = memberId,
                    EndUserId = endUserId,
                    ConsentId = consentId,
                    InstitutionId = consent.InstitutionId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.ProviderConnections.Add(connection);
            }
            connection.Status = ProviderConnectionStatus.Active;
            connection.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await queue.EnqueueInitialSync(connection, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return Results.Accepted();
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private sealed record ResetRequest(bool ConfirmReset);
}
