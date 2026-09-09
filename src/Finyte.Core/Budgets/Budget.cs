using Finyte.Core.Accounts;

namespace Finyte.Core.Budgets;

public sealed class Budget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public decimal Limit { get; set; }
    public string Currency { get; set; } = "AUD";
    public string Frequency { get; set; } = "monthly";
    public DateOnly AnchorDate { get; set; }
    public string MatchMode { get; set; } = "all";
    public string[] Categories { get; set; } = [];
    public string AccountScope { get; set; } = "analytics";
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<BudgetTag> Tags { get; set; } = [];
    public ICollection<BudgetAccount> Accounts { get; set; } = [];
}

public sealed class BudgetTag
{
    public Guid BudgetId { get; set; }
    public Budget Budget { get; set; } = null!;
    public Guid TagId { get; set; }
    public TransactionTag Tag { get; set; } = null!;
}

public sealed class BudgetAccount
{
    public Guid BudgetId { get; set; }
    public Budget Budget { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
}
