namespace Finyte.Core.Analytics;

public sealed record ProjectionRequest(Guid TenantId, string ProjectionKey, string ScopeKey, string ScopeJson);
