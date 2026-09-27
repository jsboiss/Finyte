namespace Finyte.Core.Analytics;

public sealed record OverviewProjectionScope(Guid TenantId, Guid? AccountId, string MonthKey, IReadOnlyList<Guid>? AccountIds = null, string? Label = null)
{
    public string ScopeKey => AccountId is null
        ? $"month:{MonthKey}:all"
        : $"month:{MonthKey}:account:{AccountId:D}";
}
