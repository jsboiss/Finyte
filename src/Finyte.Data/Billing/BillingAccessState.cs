namespace Finyte.Data.Billing;

public sealed record BillingAccessState(
    bool HasAccess,
    string? Status,
    string? StripePriceId,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);
