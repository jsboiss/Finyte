using System.Text.Json.Serialization;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.ProviderSync;

public sealed record FiskilWebhookRequest(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("publish_time")] DateTimeOffset? PublishTime,
    [property: JsonPropertyName("data")] FiskilWebhookData Data);

public sealed record FiskilWebhookData(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("consent_id")] string? ConsentId,
    [property: JsonPropertyName("end_user_id")] string? EndUserId,
    [property: JsonPropertyName("institution_id")] string? InstitutionId,
    [property: JsonPropertyName("account_ids")] IReadOnlyCollection<string>? AccountIds);

public sealed record FiskilWebhookIngestionResult(bool IsDuplicate, Guid? SyncRunId);

public interface IFiskilWebhookIngestor
{
    Task<FiskilWebhookIngestionResult> Ingest(FiskilWebhookRequest request, string payloadJson, CancellationToken cancellationToken);
}

public sealed class FiskilWebhookIngestor(FinyteDbContext dbContext) : IFiskilWebhookIngestor
{
    public async Task<FiskilWebhookIngestionResult> Ingest(FiskilWebhookRequest request, string payloadJson, CancellationToken cancellationToken)
    {
        var existingEvent = await dbContext.ProviderWebhookEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Provider == ProviderSyncProvider.Fiskil && x.MessageId == request.MessageId, cancellationToken);

        if (existingEvent is not null)
        {
            return new FiskilWebhookIngestionResult(IsDuplicate: true, existingEvent.SyncRunId);
        }

        var connection = string.IsNullOrWhiteSpace(request.Data.EndUserId)
            ? null
            : await dbContext.ProviderConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Provider == ProviderSyncProvider.Fiskil && x.EndUserId == request.Data.EndUserId, cancellationToken);

        var webhookEvent = new ProviderWebhookEvent
        {
            Provider = ProviderSyncProvider.Fiskil,
            MessageId = request.MessageId,
            EventType = request.Data.Event,
            TenantId = connection?.TenantId,
            PayloadJson = payloadJson,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        var dataset = ToDataset(request.Data.Event);

        if (connection is not null && dataset is not null)
        {
            var syncRun = new ProviderSyncRun
            {
                TenantId = connection.TenantId,
                Provider = ProviderSyncProvider.Fiskil,
                Dataset = dataset,
                Status = ProviderSyncStatus.Queued,
                ConsentId = request.Data.ConsentId ?? connection.ConsentId,
                EndUserId = request.Data.EndUserId,
                ExternalMessageId = request.MessageId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.ProviderSyncRuns.Add(syncRun);
            webhookEvent.SyncRunId = syncRun.Id;
        }

        dbContext.ProviderWebhookEvents.Add(webhookEvent);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new FiskilWebhookIngestionResult(IsDuplicate: false, webhookEvent.SyncRunId);
    }

    private static string? ToDataset(string eventType)
    {
        return eventType switch
        {
            "banking.accounts.sync.completed" => ProviderSyncDataset.Accounts,
            "banking.balances.sync.completed" => ProviderSyncDataset.Balances,
            "banking.transactions.basic.sync.completed" => ProviderSyncDataset.Transactions,
            "banking.transactions.sync.completed" => ProviderSyncDataset.Transactions,
            _ => null
        };
    }
}
