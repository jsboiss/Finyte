namespace Finyte.Core.HomeLoans;

public sealed class HomeLoanProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string FirstApplicant { get; set; }
    public string? SecondApplicant { get; set; }
    public Guid[] SpendingAccountIds { get; set; } = [];
    public Guid[] SavingsAccountIds { get; set; } = [];
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class HomeLoanIncomeSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string MatchKey { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public int? Applicant { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class HomeLoanCategoryChoice
{
    public Guid TenantId { get; set; }
    public Guid TagId { get; set; }
    public required string Category { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class HomeLoanPaymentCategory
{
    public Guid TenantId { get; set; }
    public Guid TransactionId { get; set; }
    public required string Category { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
