using Finyte.Core.Tenancy;

namespace Finyte.Core.ProviderSync;

public static class ProviderSyncDataset
{
    public const string Accounts = "accounts";
    public const string Balances = "balances";
    public const string Transactions = "transactions";
}

public static class ProviderSyncProvider
{
    public const string Fiskil = "fiskil";
}

public static class ProviderSyncStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class ProviderConnectionStatus
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Revoked = "revoked";
}

public sealed class ProviderConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid TenantMemberId { get; set; }
    public TenantMember? TenantMember { get; set; }
    public string Provider { get; set; } = ProviderSyncProvider.Fiskil;
    public string EndUserId { get; set; } = "";
    public string? ConsentId { get; set; }
    public string? InstitutionId { get; set; }
    public string Status { get; set; } = ProviderConnectionStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProviderAuthSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid TenantMemberId { get; set; }
    public string Provider { get; set; } = ProviderSyncProvider.Fiskil;
    public string EndUserId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ProviderWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = ProviderSyncProvider.Fiskil;
    public string MessageId { get; set; } = "";
    public string EventType { get; set; } = "";
    public Guid? TenantId { get; set; }
    public Guid? SyncRunId { get; set; }
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class ProviderSyncRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Provider { get; set; } = ProviderSyncProvider.Fiskil;
    public string Dataset { get; set; } = "";
    public string Status { get; set; } = ProviderSyncStatus.Queued;
    public string? ConsentId { get; set; }
    public string? EndUserId { get; set; }
    public string? ExternalMessageId { get; set; }
    public string? ChangeSummaryJson { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ProjectionRefreshedAt { get; set; }
}
