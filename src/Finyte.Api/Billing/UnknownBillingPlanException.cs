namespace Finyte.Api.Billing;

public sealed class UnknownBillingPlanException(string plan)
    : Exception($"Billing plan '{plan}' is not configured.")
{
    public string Plan { get; } = plan;
}
