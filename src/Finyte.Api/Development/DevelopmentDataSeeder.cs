using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Billing;
using Finyte.Core.ProviderSync;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Development;

public static class DevelopmentDataSeeder
{
    public static async Task SeedDevelopmentData(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()
            || !app.Configuration.GetValue<bool>("DevData:SeedOnStartup"))
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeedRunner>();
        await seeder.Seed(app.Lifetime.ApplicationStopping);
    }

    public static IServiceCollection AddDevelopmentDataSeeder(this IServiceCollection services)
    {
        services.AddScoped<DevelopmentDataSeedRunner>();

        return services;
    }
}

public sealed class DevelopmentDataSeedRunner(FinyteDbContext dbContext, IOverviewProjector overviewProjector)
{
    public async Task Seed(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var tenant = await GetOrCreateTenant(now, cancellationToken);
        await SeedBilling(tenant.Id, now, cancellationToken);
        var accounts = await SeedAccounts(tenant.Id, now, cancellationToken);
        await SeedTransactions(tenant.Id, accounts, now, cancellationToken);
        var tags = await SeedTags(tenant.Id, now, cancellationToken);
        await SeedTransactionTags(tenant.Id, tags, now, cancellationToken);
        await SeedMerchantRules(tenant.Id, tags, now, cancellationToken);
        await SeedProviderSync(tenant.Id, now, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        await overviewProjector.Rebuild(new OverviewProjectionScope(tenant.Id, null, GetCurrentMonthKey(now)), cancellationToken);

        foreach (var account in accounts)
        {
            await overviewProjector.Rebuild(new OverviewProjectionScope(tenant.Id, account.Id, GetCurrentMonthKey(now)), cancellationToken);
        }
    }

    private async Task<Tenant> GetOrCreateTenant(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenant = await dbContext.TenantMembers
            .Include(x => x.Tenant)
            .Where(x => x.UserId == "dev-user")
            .Select(x => x.Tenant)
            .SingleOrDefaultAsync(cancellationToken);

        if (tenant is not null)
        {
            tenant.ClerkOrganizationId = "org_dev-family";
            tenant.Name = "Dev household";
            return tenant;
        }

        tenant = new Tenant
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ClerkOrganizationId = "org_dev-family",
            Name = "Dev household",
            CreatedAt = now.AddMonths(-3)
        };

        dbContext.Tenants.Add(tenant);
        dbContext.TenantMembers.Add(new TenantMember
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111112"),
            TenantId = tenant.Id,
            UserId = "dev-user",
            Role = TenantRole.Owner,
            CreatedAt = now.AddMonths(-3)
        });

        return tenant;
    }

    private async Task SeedBilling(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var customer = await dbContext.BillingCustomers
            .Include(x => x.Subscriptions)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        if (customer is null)
        {
            customer = new BillingCustomer
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222221"),
                TenantId = tenantId,
                StripeCustomerId = "cus_dev_seed",
                CreatedAt = now.AddMonths(-2)
            };

            dbContext.BillingCustomers.Add(customer);
        }

        var subscription = customer.Subscriptions.SingleOrDefault(x => x.StripeSubscriptionId == "sub_dev_seed");
        if (subscription is null)
        {
            dbContext.BillingSubscriptions.Add(new BillingSubscription
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TenantId = tenantId,
                BillingCustomerId = customer.Id,
                StripeSubscriptionId = "sub_dev_seed",
                StripeCustomerId = customer.StripeCustomerId,
                StripePriceId = "price_dev_monthly",
                Status = "active",
                CurrentPeriodStart = now.AddDays(-12),
                CurrentPeriodEnd = now.AddDays(18),
                CancelAtPeriodEnd = false,
                CreatedAt = now.AddMonths(-2),
                UpdatedAt = now
            });
        }
        else
        {
            subscription.Status = "active";
            subscription.CurrentPeriodStart = now.AddDays(-12);
            subscription.CurrentPeriodEnd = now.AddDays(18);
            subscription.CancelAtPeriodEnd = false;
            subscription.UpdatedAt = now;
        }
    }

    private async Task<IReadOnlyList<Account>> SeedAccounts(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existingAccounts = await dbContext.Accounts
            .Where(x => x.TenantId == tenantId && x.FiskilAccountId != null && x.FiskilAccountId.StartsWith("dev_"))
            .ToDictionaryAsync(x => x.FiskilAccountId!, cancellationToken);
        var seeds = new[]
        {
            new AccountSeed(
                Guid.Parse("33333333-3333-3333-3333-333333333331"),
                "dev_everyday",
                "Everyday Spending",
                "Complete Access",
                "transaction",
                "063-000",
                "12345678",
                4287.35m,
                4287.35m,
                null),
            new AccountSeed(
                Guid.Parse("33333333-3333-3333-3333-333333333332"),
                "dev_savings",
                "Offset Savings",
                "Bonus Saver",
                "savings",
                "063-000",
                "87654321",
                18240.10m,
                18240.10m,
                null),
            new AccountSeed(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "dev_credit",
                "Rewards Credit Card",
                "Platinum Rewards",
                "credit-card",
                null,
                "4444333322221111",
                -1264.82m,
                8735.18m,
                10000m)
        };
        var accounts = new List<Account>();

        foreach (var seed in seeds)
        {
            if (!existingAccounts.TryGetValue(seed.FiskilAccountId, out var account))
            {
                account = new Account
                {
                    Id = seed.Id,
                    TenantId = tenantId,
                    FiskilAccountId = seed.FiskilAccountId,
                    Name = seed.Name,
                    CreatedAt = now.AddMonths(-3)
                };

                dbContext.Accounts.Add(account);
            }

            account.Name = seed.Name;
            account.ProductName = seed.ProductName;
            account.ProductCategory = seed.ProductCategory;
            account.Bsb = seed.Bsb;
            account.AccountNumber = seed.AccountNumber;
            account.InstitutionId = "dev-bank";
            account.ConsentId = "dev-consent";
            account.IsOwned = true;
            account.OpenStatus = "open";
            account.CreationDate = DateOnly.FromDateTime(now.AddYears(-2).UtcDateTime);
            account.CurrentBalance = seed.CurrentBalance;
            account.AvailableBalance = seed.AvailableBalance;
            account.CreditLimit = seed.CreditLimit;
            account.Currency = "AUD";
            account.BalanceAsOf = now.AddMinutes(-15);
            accounts.Add(account);
        }

        return accounts;
    }

    private async Task SeedTransactions(Guid tenantId, IReadOnlyList<Account> accounts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var accountMap = accounts.ToDictionary(x => x.FiskilAccountId!);
        var existingTransactions = await dbContext.Transactions
            .Where(x => x.TenantId == tenantId && x.FiskilTransactionId.StartsWith("dev_"))
            .ToDictionaryAsync(x => x.FiskilTransactionId, cancellationToken);
        var seeds = CreateTransactionSeeds(now, accountMap);

        foreach (var seed in seeds)
        {
            if (!existingTransactions.TryGetValue(seed.FiskilTransactionId, out var transaction))
            {
                transaction = new Transaction
                {
                    Id = seed.Id,
                    TenantId = tenantId,
                    FiskilTransactionId = seed.FiskilTransactionId,
                    CreatedAt = seed.PostedAt.AddHours(2)
                };

                dbContext.Transactions.Add(transaction);
            }

            transaction.AccountId = seed.AccountId;
            transaction.Amount = seed.Amount;
            transaction.Currency = "AUD";
            transaction.Description = seed.Description;
            transaction.Status = "posted";
            transaction.PostedAt = seed.PostedAt;
            transaction.ExecutedAt = seed.PostedAt.AddHours(-3);
            transaction.PrimaryCategory = seed.PrimaryCategory;
            transaction.SecondaryCategory = seed.SecondaryCategory;
            transaction.MerchantName = seed.MerchantName;
            transaction.Reference = seed.Reference;
            transaction.RawJson = $$"""{"source":"development-seed","merchant":"{{seed.MerchantName}}"}""";
        }
    }

    private async Task<IReadOnlyDictionary<string, TransactionTag>> SeedTags(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existingTags = await dbContext.TransactionTags
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Name, cancellationToken);
        var seeds = new[]
        {
            new TagSeed(Guid.Parse("44444444-4444-4444-4444-444444444441"), "Essentials", "#bbf7d0"),
            new TagSeed(Guid.Parse("44444444-4444-4444-4444-444444444442"), "Home", "#bae6fd"),
            new TagSeed(Guid.Parse("44444444-4444-4444-4444-444444444443"), "Lifestyle", "#ddd6fe"),
            new TagSeed(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Transfers", "#fed7aa")
        };
        var tags = new Dictionary<string, TransactionTag>();

        foreach (var seed in seeds)
        {
            if (!existingTags.TryGetValue(seed.Name, out var tag))
            {
                tag = new TransactionTag
                {
                    Id = seed.Id,
                    TenantId = tenantId,
                    Name = seed.Name,
                    Color = seed.Color,
                    CreatedAt = now.AddMonths(-1)
                };

                dbContext.TransactionTags.Add(tag);
            }

            tag.Color = seed.Color;
            tags[seed.Name] = tag;
        }

        return tags;
    }

    private async Task SeedTransactionTags(
        Guid tenantId,
        IReadOnlyDictionary<string, TransactionTag> tags,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var transactionTagNames = new Dictionary<string, string[]>
        {
            ["dev_rent"] = ["Home"],
            ["dev_groceries_1"] = ["Essentials"],
            ["dev_groceries_2"] = ["Essentials"],
            ["dev_train"] = ["Essentials"],
            ["dev_fuel"] = ["Essentials"],
            ["dev_utilities"] = ["Home"],
            ["dev_phone"] = ["Essentials"],
            ["dev_dining"] = ["Lifestyle"],
            ["dev_streaming"] = ["Lifestyle"],
            ["dev_pharmacy"] = ["Essentials"],
            ["dev_hardware"] = ["Home"],
            ["dev_transfer_savings"] = ["Transfers"],
            ["dev_savings_transfer"] = ["Transfers"],
            ["dev_card_payment"] = ["Transfers"],
            ["dev_credit_payment"] = ["Transfers"]
        };
        var transactions = await dbContext.Transactions
            .Where(x => x.TenantId == tenantId && transactionTagNames.Keys.Contains(x.FiskilTransactionId))
            .ToDictionaryAsync(x => x.FiskilTransactionId, cancellationToken);
        var transactionIds = transactions.Values.Select(x => x.Id).ToList();
        var existingAssignments = await dbContext.TransactionTagAssignments
            .Where(x => transactionIds.Contains(x.TransactionId))
            .Select(x => new { x.TransactionId, x.TagId })
            .ToListAsync(cancellationToken);
        var existingKeys = existingAssignments
            .Select(x => $"{x.TransactionId:N}:{x.TagId:N}")
            .ToHashSet();

        foreach (var pair in transactionTagNames)
        {
            if (!transactions.TryGetValue(pair.Key, out var transaction))
            {
                continue;
            }

            foreach (var tagName in pair.Value)
            {
                var tag = tags[tagName];
                var key = $"{transaction.Id:N}:{tag.Id:N}";
                if (existingKeys.Contains(key))
                {
                    continue;
                }

                dbContext.TransactionTagAssignments.Add(new TransactionTagAssignment
                {
                    TransactionId = transaction.Id,
                    TagId = tag.Id,
                    CreatedAt = now
                });
            }
        }
    }

    private async Task SeedMerchantRules(
        Guid tenantId,
        IReadOnlyDictionary<string, TransactionTag> tags,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var seeds = new[]
        {
            new MerchantRuleSeed(Guid.Parse("44444444-4444-4444-4444-444444444451"), "Woolworths", "woolworths", "Essentials"),
            new MerchantRuleSeed(Guid.Parse("44444444-4444-4444-4444-444444444452"), "Coles", "coles", "Essentials"),
            new MerchantRuleSeed(Guid.Parse("44444444-4444-4444-4444-444444444453"), "Bunnings", "bunnings", "Home"),
            new MerchantRuleSeed(Guid.Parse("44444444-4444-4444-4444-444444444454"), "Netflix", "netflix", "Lifestyle")
        };
        var existingRules = await dbContext.MerchantTagRules
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => $"{x.MerchantKey}:{x.TagId:N}", cancellationToken);

        foreach (var seed in seeds)
        {
            var tag = tags[seed.TagName];
            var key = $"{seed.MerchantKey}:{tag.Id:N}";
            if (existingRules.ContainsKey(key))
            {
                continue;
            }

            dbContext.MerchantTagRules.Add(new MerchantTagRule
            {
                Id = seed.Id,
                TenantId = tenantId,
                MerchantName = seed.MerchantName,
                MerchantKey = seed.MerchantKey,
                TagId = tag.Id,
                CreatedAt = now
            });
        }
    }

    private async Task SeedProviderSync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenantMemberId = await dbContext.TenantMembers
            .Where(x => x.TenantId == tenantId && x.UserId == "dev-user")
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);
        var connection = await dbContext.ProviderConnections
            .SingleOrDefaultAsync(x => x.Provider == ProviderSyncProvider.Fiskil && x.EndUserId == "dev-user", cancellationToken);

        if (connection is null)
        {
            dbContext.ProviderConnections.Add(new ProviderConnection
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555551"),
                TenantId = tenantId,
                TenantMemberId = tenantMemberId,
                Provider = ProviderSyncProvider.Fiskil,
                EndUserId = "dev-user",
                ConsentId = "dev-consent",
                InstitutionId = "dev-bank",
                Status = ProviderConnectionStatus.Active,
                CreatedAt = now.AddDays(-20),
                UpdatedAt = now
            });
        }
        else
        {
            connection.TenantId = tenantId;
            connection.TenantMemberId = tenantMemberId;
            connection.ConsentId = "dev-consent";
            connection.InstitutionId = "dev-bank";
            connection.Status = ProviderConnectionStatus.Active;
            connection.UpdatedAt = now;
        }

        if (!await dbContext.ProviderSyncRuns.AnyAsync(x => x.ExternalMessageId == "dev-sync-run", cancellationToken))
        {
            dbContext.ProviderSyncRuns.Add(new ProviderSyncRun
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555552"),
                TenantId = tenantId,
                Provider = ProviderSyncProvider.Fiskil,
                Dataset = ProviderSyncDataset.Transactions,
                Status = ProviderSyncStatus.Succeeded,
                ConsentId = "dev-consent",
                EndUserId = "dev-user",
                ExternalMessageId = "dev-sync-run",
                ChangeSummaryJson = """{"accountsCreated":3,"transactionsCreated":18}""",
                CreatedAt = now.AddMinutes(-30),
                StartedAt = now.AddMinutes(-29),
                CompletedAt = now.AddMinutes(-28),
                ProjectionRefreshedAt = now.AddMinutes(-27)
            });
        }
    }

    private static IReadOnlyList<TransactionSeed> CreateTransactionSeeds(DateTimeOffset now, IReadOnlyDictionary<string, Account> accounts)
    {
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        return
        [
            CreateTransaction("dev_salary", accounts["dev_everyday"].Id, 1, 4850.00m, "Salary payment", "Income", "Salary", "Northwind Payroll", "PAYROLL", monthStart),
            CreateTransaction("dev_rent", accounts["dev_everyday"].Id, 2, -2650.00m, "Monthly rent", "Housing", "Rent", "Smith Property Group", "RENT", monthStart),
            CreateTransaction("dev_groceries_1", accounts["dev_everyday"].Id, 3, -184.30m, "Weekly groceries", "Food", "Groceries", "Woolworths", "GROCERIES", monthStart),
            CreateTransaction("dev_train", accounts["dev_everyday"].Id, 4, -42.80m, "Public transport top up", "Transport", "Transit", "Translink", "GO CARD", monthStart),
            CreateTransaction("dev_coffee", accounts["dev_everyday"].Id, 5, -18.50m, "Coffee meeting", "Food", "Cafe", "Little Collins Cafe", "COFFEE", monthStart),
            CreateTransaction("dev_transfer_savings", accounts["dev_everyday"].Id, 6, -750.00m, "Transfer to savings", "Transfer", "Savings", "Offset Savings", "TRANSFER", monthStart),
            CreateTransaction("dev_savings_transfer", accounts["dev_savings"].Id, 6, 750.00m, "Transfer from everyday", "Transfer", "Savings", "Everyday Spending", "TRANSFER", monthStart),
            CreateTransaction("dev_groceries_2", accounts["dev_credit"].Id, 7, -126.74m, "Supermarket shop", "Food", "Groceries", "Coles", "GROCERIES", monthStart),
            CreateTransaction("dev_fuel", accounts["dev_credit"].Id, 8, -91.20m, "Fuel", "Transport", "Fuel", "Ampol", "FUEL", monthStart),
            CreateTransaction("dev_utilities", accounts["dev_everyday"].Id, 9, -214.65m, "Electricity bill", "Bills", "Utilities", "Energex", "POWER", monthStart),
            CreateTransaction("dev_phone", accounts["dev_credit"].Id, 10, -69.00m, "Mobile plan", "Bills", "Phone", "Telstra", "PHONE", monthStart),
            CreateTransaction("dev_dining", accounts["dev_credit"].Id, 11, -88.40m, "Dinner", "Food", "Dining", "Donna Chang", "DINING", monthStart),
            CreateTransaction("dev_interest", accounts["dev_savings"].Id, 12, 43.87m, "Monthly interest", "Income", "Interest", "Bank Interest", "INTEREST", monthStart),
            CreateTransaction("dev_streaming", accounts["dev_credit"].Id, 13, -21.99m, "Streaming subscription", "Entertainment", "Subscriptions", "Netflix", "SUBSCRIPTION", monthStart),
            CreateTransaction("dev_pharmacy", accounts["dev_everyday"].Id, 14, -36.55m, "Pharmacy", "Health", "Pharmacy", "Chemist Warehouse", "PHARMACY", monthStart),
            CreateTransaction("dev_card_payment", accounts["dev_everyday"].Id, 15, -900.00m, "Credit card payment", "Transfer", "Card Payment", "Rewards Credit Card", "CARD PAYMENT", monthStart),
            CreateTransaction("dev_credit_payment", accounts["dev_credit"].Id, 15, 900.00m, "Payment received", "Transfer", "Card Payment", "Everyday Spending", "CARD PAYMENT", monthStart),
            CreateTransaction("dev_hardware", accounts["dev_credit"].Id, 16, -142.95m, "Home repairs", "Home", "Hardware", "Bunnings", "HARDWARE", monthStart)
        ];
    }

    private static TransactionSeed CreateTransaction(
        string fiskilTransactionId,
        Guid accountId,
        int day,
        decimal amount,
        string description,
        string primaryCategory,
        string secondaryCategory,
        string merchantName,
        string reference,
        DateTimeOffset monthStart)
    {
        var maxDay = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        var postedAt = monthStart.AddDays(Math.Min(day, maxDay) - 1).AddHours(10);

        return new TransactionSeed(
            Guid.CreateVersion7(),
            fiskilTransactionId,
            accountId,
            amount,
            description,
            primaryCategory,
            secondaryCategory,
            merchantName,
            reference,
            postedAt);
    }

    private static string GetCurrentMonthKey(DateTimeOffset now)
    {
        return $"{now.Year:D4}-{now.Month:D2}";
    }

    private sealed record AccountSeed(
        Guid Id,
        string FiskilAccountId,
        string Name,
        string ProductName,
        string ProductCategory,
        string? Bsb,
        string AccountNumber,
        decimal CurrentBalance,
        decimal AvailableBalance,
        decimal? CreditLimit);

    private sealed record TransactionSeed(
        Guid Id,
        string FiskilTransactionId,
        Guid AccountId,
        decimal Amount,
        string Description,
        string PrimaryCategory,
        string SecondaryCategory,
        string MerchantName,
        string Reference,
        DateTimeOffset PostedAt);

    private sealed record TagSeed(Guid Id, string Name, string Color);

    private sealed record MerchantRuleSeed(Guid Id, string MerchantName, string MerchantKey, string TagName);
}
