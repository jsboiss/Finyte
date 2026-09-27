using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Billing;
using Finyte.Core.Budgets;
using Finyte.Core.ProviderSync;
using Finyte.Core.Recurring;
using Finyte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data;

public class FinyteDbContext(DbContextOptions<FinyteDbContext> options) : DbContext(options)
{
    public DbSet<Finyte.Core.PayCycles.PayCycleProfile> PayCycleProfiles => Set<Finyte.Core.PayCycles.PayCycleProfile>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<RecurringPaymentSeries> RecurringPaymentSeries => Set<RecurringPaymentSeries>();

    public DbSet<RecurringPaymentAlias> RecurringPaymentAliases => Set<RecurringPaymentAlias>();

    public DbSet<RecurringPaymentDecision> RecurringPaymentDecisions => Set<RecurringPaymentDecision>();

    public DbSet<RecurringPaymentReview> RecurringPaymentReviews => Set<RecurringPaymentReview>();

    public DbSet<RecurringDiscoveryDecision> RecurringDiscoveryDecisions => Set<RecurringDiscoveryDecision>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TransactionFileImport> TransactionFileImports => Set<TransactionFileImport>();

    public DbSet<TransactionFileIdentity> TransactionFileIdentities => Set<TransactionFileIdentity>();

    public DbSet<TransactionTag> TransactionTags => Set<TransactionTag>();

    public DbSet<TransactionTagAssignment> TransactionTagAssignments => Set<TransactionTagAssignment>();
    public DbSet<TransactionTagExclusion> TransactionTagExclusions => Set<TransactionTagExclusion>();

    public DbSet<MerchantTagRule> MerchantTagRules => Set<MerchantTagRule>();

    public DbSet<OverviewProjection> OverviewProjections => Set<OverviewProjection>();

    public DbSet<BillingCustomer> BillingCustomers => Set<BillingCustomer>();

    public DbSet<BillingSubscription> BillingSubscriptions => Set<BillingSubscription>();

    public DbSet<BillingEvent> BillingEvents => Set<BillingEvent>();

    public DbSet<ProviderConnection> ProviderConnections => Set<ProviderConnection>();

    public DbSet<ProviderAuthSession> ProviderAuthSessions => Set<ProviderAuthSession>();

    public DbSet<ProviderWebhookEvent> ProviderWebhookEvents => Set<ProviderWebhookEvent>();

    public DbSet<ProviderSyncRun> ProviderSyncRuns => Set<ProviderSyncRun>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantMember> TenantMembers => Set<TenantMember>();

    public DbSet<ClerkWebhookEvent> ClerkWebhookEvents => Set<ClerkWebhookEvent>();

    public DbSet<FamilyInvitation> FamilyInvitations => Set<FamilyInvitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Finyte.Core.PayCycles.PayCycleProfile>(x =>
        {
            x.ToTable("pay_cycle_profiles");
            x.HasKey(y => y.Id);
            x.Property(y => y.Name).HasMaxLength(120).IsRequired();
            x.Property(y => y.Frequency).HasMaxLength(16).IsRequired();
            x.Property(y => y.Currency).HasMaxLength(3).IsRequired();
            x.Property(y => y.ExpectedIncome).HasPrecision(18, 2);
            x.Property(y => y.AccountIds).HasColumnType("uuid[]");
            x.Property(y => y.SavingsAccountIds).HasColumnType("uuid[]");
            x.Property(y => y.Version).IsConcurrencyToken();
            x.HasIndex(y => new { y.TenantId, y.Name });
        });
        modelBuilder.Entity<Budget>(x =>
        {
            x.ToTable("budgets");
            x.HasKey(y => y.Id);
            x.Property(y => y.Name).HasMaxLength(120).IsRequired();
            x.Property(y => y.Limit).HasPrecision(18, 2);
            x.Property(y => y.Currency).HasMaxLength(3).IsRequired();
            x.Property(y => y.Frequency).HasMaxLength(16).IsRequired();
            x.Property(y => y.MatchMode).HasMaxLength(16).IsRequired();
            x.Property(y => y.AccountScope).HasMaxLength(16).IsRequired();
            x.Property(y => y.Categories).HasColumnType("text[]");
            x.Property(y => y.Version).IsConcurrencyToken();
            x.HasIndex(y => new { y.TenantId, y.Name });
        });
        modelBuilder.Entity<BudgetTag>(x =>
        {
            x.ToTable("budget_tags");
            x.HasKey(y => new { y.BudgetId, y.TagId });
            x.HasOne(y => y.Budget).WithMany(y => y.Tags).HasForeignKey(y => y.BudgetId).OnDelete(DeleteBehavior.Cascade);
            x.HasOne(y => y.Tag).WithMany().HasForeignKey(y => y.TagId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<BudgetAccount>(x =>
        {
            x.ToTable("budget_accounts");
            x.HasKey(y => new { y.BudgetId, y.AccountId });
            x.HasOne(y => y.Budget).WithMany(y => y.Accounts).HasForeignKey(y => y.BudgetId).OnDelete(DeleteBehavior.Cascade);
            x.HasOne(y => y.Account).WithMany().HasForeignKey(y => y.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        Recurring.RecurringPaymentConfiguration.Configure(modelBuilder);
        modelBuilder.Entity<TransactionFileImport>(x =>
        {
            x.ToTable("transaction_file_imports");
            x.HasKey(y => y.Id);
            x.Property(y => y.FileName).HasMaxLength(255).IsRequired();
            x.Property(y => y.Status).HasMaxLength(32).IsRequired();
            x.Property(y => y.Error).HasMaxLength(2048);
            x.HasIndex(y => new { y.TenantId, y.StartedAt });
            x.HasOne<Account>().WithMany().HasForeignKey(y => y.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TransactionFileIdentity>(x =>
        {
            x.ToTable("transaction_file_identities");
            x.HasKey(y => new { y.TenantId, y.ExternalId });
            x.Property(y => y.ExternalId).HasMaxLength(128);
            x.HasOne<Transaction>().WithMany().HasForeignKey(y => y.TransactionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Account>(x =>
        {
            x.ToTable("accounts");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.ProviderConnectionId);

            x.Property(y => y.FiskilAccountId)
                .HasMaxLength(128);

            x.Property(y => y.AccountNumber)
                .HasMaxLength(64);

            x.Property(y => y.Bsb)
                .HasMaxLength(16);

            x.Property(y => y.Name)
                .HasMaxLength(120)
                .IsRequired();

            x.Property(y => y.ProductName)
                .HasMaxLength(256);

            x.Property(y => y.CustomName).HasMaxLength(120);
            x.Property(y => y.AccountTypeOverride).HasMaxLength(32);
            x.Property(y => y.PreferencesVersion).IsConcurrencyToken();
            x.Property(y => y.ManualBalanceVersion).IsConcurrencyToken();

            x.Property(y => y.ProductCategory)
                .HasMaxLength(128);

            x.Property(y => y.InstitutionId)
                .HasMaxLength(128);

            x.Property(y => y.ConsentId)
                .HasMaxLength(128);

            x.Property(y => y.OpenStatus)
                .HasMaxLength(32);

            x.Property(y => y.CurrentBalance)
                .HasPrecision(18, 2);

            x.Property(y => y.AvailableBalance)
                .HasPrecision(18, 2);

            x.Property(y => y.CreditLimit)
                .HasPrecision(18, 2);

            x.Property(y => y.Currency)
                .HasMaxLength(3)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.ProviderConnectionId);
            x.HasOne<ProviderConnection>()
                .WithMany()
                .HasForeignKey(y => y.ProviderConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
            x.HasIndex(y => new { y.TenantId, y.FiskilAccountId })
                .IsUnique()
                .HasFilter("\"FiskilAccountId\" IS NOT NULL");
        });

        modelBuilder.Entity<Transaction>(x =>
        {
            x.ToTable("transactions");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.FiskilTransactionId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.Amount)
                .HasPrecision(18, 2);

            x.Property(y => y.Currency)
                .HasMaxLength(3)
                .IsRequired();

            x.Property(y => y.Description)
                .HasMaxLength(512);

            x.Property(y => y.Status)
                .HasMaxLength(64);

            x.Property(y => y.PrimaryCategory)
                .HasMaxLength(128);

            x.Property(y => y.SecondaryCategory)
                .HasMaxLength(128);

            x.Property(y => y.MerchantName)
                .HasMaxLength(256);

            x.Property(y => y.Reference)
                .HasMaxLength(256);

            x.Property(y => y.RawJson)
                .HasColumnType("jsonb");

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasOne(y => y.Account)
                .WithMany(y => y.Transactions)
                .HasForeignKey(y => y.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            x.Property(y => y.InternalTransferSource)
                .HasMaxLength(16);

            x.HasOne(y => y.InternalTransferAccount)
                .WithMany()
                .HasForeignKey(y => y.InternalTransferAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            x.HasIndex(y => new { y.TenantId, y.InternalTransferAccountId });

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.AccountId);
            x.HasIndex(y => new { y.TenantId, y.PostedAt });
            x.HasIndex(y => new { y.TenantId, y.AccountId, y.PostedAt });
            x.HasIndex(y => new { y.TenantId, y.FiskilTransactionId })
                .IsUnique();
            x.HasIndex(y => y.PostedAt);
        });

        modelBuilder.Entity<TransactionTag>(x =>
        {
            x.ToTable("transaction_tags");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.Name)
                .HasMaxLength(80)
                .IsRequired();

            x.Property(y => y.Color)
                .HasMaxLength(16)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => new { y.TenantId, y.Name })
                .IsUnique();
        });

        modelBuilder.Entity<TransactionTagAssignment>(x =>
        {
            x.ToTable("transaction_tag_assignments");

            x.HasKey(y => new { y.TransactionId, y.TagId });

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.Property(y => y.Source)
                .HasMaxLength(24)
                .HasDefaultValue(TransactionTagSource.Legacy)
                .IsRequired();

            x.HasOne(y => y.MerchantRule)
                .WithMany()
                .HasForeignKey(y => y.MerchantRuleId)
                .OnDelete(DeleteBehavior.SetNull);

            x.HasOne(y => y.Transaction)
                .WithMany(y => y.TagAssignments)
                .HasForeignKey(y => y.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);

            x.HasOne(y => y.Tag)
                .WithMany(y => y.TransactionAssignments)
                .HasForeignKey(y => y.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            x.HasIndex(y => y.TagId);
        });

        modelBuilder.Entity<TransactionTagExclusion>(x =>
        {
            x.ToTable("transaction_tag_exclusions");
            x.HasKey(y => new { y.TransactionId, y.TagId });
            x.HasOne(y => y.Transaction)
                .WithMany(y => y.TagExclusions)
                .HasForeignKey(y => y.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
            x.HasOne(y => y.Tag)
                .WithMany()
                .HasForeignKey(y => y.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MerchantTagRule>(x =>
        {
            x.ToTable("merchant_tag_rules");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.MerchantName)
                .HasMaxLength(256)
                .IsRequired();

            x.Property(y => y.MerchantKey)
                .HasMaxLength(256)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasOne(y => y.Tag)
                .WithMany(y => y.MerchantRules)
                .HasForeignKey(y => y.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.TagId);
            x.HasIndex(y => new { y.TenantId, y.MerchantKey, y.TagId })
                .IsUnique();
        });

        modelBuilder.Entity<OverviewProjection>(x =>
        {
            x.ToTable("overview_projections");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.MonthKey)
                .HasMaxLength(7)
                .IsRequired();

            x.Property(y => y.Currency)
                .HasMaxLength(3)
                .IsRequired();

            x.Property(y => y.PayloadJson)
                .HasColumnType("jsonb")
                .IsRequired();

            x.Property(y => y.Status)
                .HasMaxLength(32)
                .IsRequired();

            x.Property(y => y.Generation)
                .IsConcurrencyToken();

            x.Property(y => y.LastError)
                .HasMaxLength(2048);

            x.Property(y => y.TemporalWorkflowId)
                .HasMaxLength(256);

            x.Property(y => y.CalculatedAt)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.Property(y => y.UpdatedAt)
                .IsRequired();

            x.HasIndex(y => new { y.TenantId, y.AccountId, y.MonthKey })
                .IsUnique()
                .HasFilter("\"AccountId\" IS NOT NULL");
            x.HasIndex(y => new { y.TenantId, y.MonthKey })
                .IsUnique()
                .HasFilter("\"AccountId\" IS NULL");
            x.HasIndex(y => new { y.Status, y.DispatchedAt });
        });

        modelBuilder.Entity<BillingCustomer>(x =>
        {
            x.ToTable("billing_customers");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.StripeCustomerId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasOne(y => y.Tenant)
                .WithOne(y => y.BillingCustomer)
                .HasForeignKey<BillingCustomer>(y => y.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            x.HasIndex(y => y.TenantId)
                .IsUnique();
            x.HasIndex(y => y.StripeCustomerId)
                .IsUnique();
        });

        modelBuilder.Entity<BillingSubscription>(x =>
        {
            x.ToTable("billing_subscriptions");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.BillingCustomerId)
                .IsRequired();

            x.Property(y => y.StripeSubscriptionId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.StripeCustomerId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.StripePriceId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.Status)
                .HasMaxLength(32)
                .IsRequired();

            x.Property(y => y.CancelAtPeriodEnd)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.Property(y => y.UpdatedAt)
                .IsRequired();

            x.HasOne(y => y.Tenant)
                .WithMany(y => y.BillingSubscriptions)
                .HasForeignKey(y => y.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            x.HasOne(y => y.BillingCustomer)
                .WithMany(y => y.Subscriptions)
                .HasForeignKey(y => y.BillingCustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.BillingCustomerId);
            x.HasIndex(y => y.StripeCustomerId);
            x.HasIndex(y => y.StripeSubscriptionId)
                .IsUnique();
        });

        modelBuilder.Entity<BillingEvent>(x =>
        {
            x.ToTable("billing_events");

            x.HasKey(y => y.Id);

            x.Property(y => y.StripeEventId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.Type)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.PayloadJson)
                .HasColumnType("jsonb")
                .IsRequired();

            x.Property(y => y.ProcessedAt)
                .IsRequired();

            x.HasIndex(y => y.StripeEventId)
                .IsUnique();
            x.HasIndex(y => y.Type);
        });

        modelBuilder.Entity<ProviderConnection>(x =>
        {
            x.ToTable("provider_connections");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.TenantMemberId)
                .IsRequired();

            x.Property(y => y.Provider)
                .HasMaxLength(64)
                .IsRequired();

            x.Property(y => y.EndUserId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.ConsentId)
                .HasMaxLength(128);

            x.Property(y => y.InstitutionId)
                .HasMaxLength(128);

            x.Property(y => y.Status)
                .HasMaxLength(32)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.Property(y => y.UpdatedAt)
                .IsRequired();

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.TenantMemberId);
            x.HasOne(y => y.TenantMember)
                .WithMany()
                .HasForeignKey(y => y.TenantMemberId)
                .OnDelete(DeleteBehavior.Restrict);
            x.HasIndex(y => new { y.Provider, y.EndUserId });
            x.HasIndex(y => new { y.Provider, y.ConsentId })
                .IsUnique()
                .HasFilter("\"ConsentId\" IS NOT NULL");
        });

        modelBuilder.Entity<ProviderAuthSession>(x =>
        {
            x.ToTable("provider_auth_sessions");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.TenantMemberId)
                .IsRequired();

            x.Property(y => y.Provider)
                .HasMaxLength(64)
                .IsRequired();

            x.Property(y => y.EndUserId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.SessionId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.ExpiresAt)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.TenantMemberId);
            x.HasIndex(y => new { y.Provider, y.SessionId })
                .IsUnique();

            x.HasOne<TenantMember>()
                .WithMany()
                .HasForeignKey(y => y.TenantMemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProviderWebhookEvent>(x =>
        {
            x.ToTable("provider_webhook_events");

            x.HasKey(y => y.Id);

            x.Property(y => y.Provider)
                .HasMaxLength(64)
                .IsRequired();

            x.Property(y => y.MessageId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.EventType)
                .HasMaxLength(160)
                .IsRequired();

            x.Property(y => y.PayloadJson)
                .HasColumnType("jsonb")
                .IsRequired();

            x.Property(y => y.ReceivedAt)
                .IsRequired();

            x.HasIndex(y => new { y.Provider, y.MessageId })
                .IsUnique();
            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.SyncRunId);
            x.HasIndex(y => y.EventType);
        });

        modelBuilder.Entity<ProviderSyncRun>(x =>
        {
            x.ToTable("provider_sync_runs");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.Provider)
                .HasMaxLength(64)
                .IsRequired();

            x.Property(y => y.Dataset)
                .HasMaxLength(64)
                .IsRequired();

            x.Property(y => y.Status)
                .HasMaxLength(32)
                .IsRequired();

            x.Property(y => y.ConsentId)
                .HasMaxLength(128);

            x.Property(y => y.EndUserId)
                .HasMaxLength(128);

            x.Property(y => y.ExternalMessageId)
                .HasMaxLength(128);

            x.Property(y => y.ChangeSummaryJson)
                .HasColumnType("jsonb");

            x.Property(y => y.Error)
                .HasMaxLength(2048);

            x.Property(y => y.TemporalWorkflowId)
                .HasMaxLength(256);

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.BatchId);
            x.HasIndex(y => new { y.TenantId, y.Status });
            x.HasIndex(y => new { y.Status, y.DispatchedAt });
            x.HasIndex(y => y.ExternalMessageId);
        });

        modelBuilder.Entity<Tenant>(x =>
        {
            x.ToTable("tenants");

            x.HasKey(y => y.Id);

            x.Property(y => y.ClerkOrganizationId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.Name)
                .HasMaxLength(160)
                .IsRequired();

            x.Property(y => y.FinancialDataVersion)
                .IsConcurrencyToken();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasIndex(y => y.ClerkOrganizationId)
                .IsUnique();
        });

        modelBuilder.Entity<TenantMember>(x =>
        {
            x.ToTable("tenant_members");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.UserId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.DisplayName)
                .HasMaxLength(160);

            x.Property(y => y.Email)
                .HasMaxLength(320);

            x.Property(y => y.ClerkMembershipId)
                .HasMaxLength(128);

            x.Property(y => y.Role)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasOne(y => y.Tenant)
                .WithMany(y => y.Members)
                .HasForeignKey(y => y.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => new { y.TenantId, y.UserId })
                .IsUnique();
            x.HasIndex(y => y.ClerkMembershipId)
                .IsUnique()
                .HasFilter("\"ClerkMembershipId\" IS NOT NULL");
        });

        modelBuilder.Entity<ClerkWebhookEvent>(x =>
        {
            x.ToTable("clerk_webhook_events");

            x.HasKey(y => y.Id);

            x.Property(y => y.MessageId)
                .HasMaxLength(128)
                .IsRequired();

            x.Property(y => y.EventType)
                .HasMaxLength(160)
                .IsRequired();

            x.Property(y => y.ProcessedAt)
                .IsRequired();

            x.HasIndex(y => y.MessageId)
                .IsUnique();
        });

        modelBuilder.Entity<FamilyInvitation>(x =>
        {
            x.ToTable("family_invitations");
            x.HasKey(y => y.Id);
            x.Property(y => y.ProviderInvitationId).HasMaxLength(128).IsRequired();
            x.Property(y => y.Email).HasMaxLength(320).IsRequired();
            x.Property(y => y.Role).HasMaxLength(64).IsRequired();
            x.Property(y => y.Status).HasMaxLength(32).IsRequired();
            x.Property(y => y.InvitedByUserId).HasMaxLength(128).IsRequired();
            x.Property(y => y.CreatedAt).IsRequired();
            x.Property(y => y.UpdatedAt).IsRequired();
            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.ProviderInvitationId).IsUnique();
            x.HasIndex(y => new { y.TenantId, y.Email, y.Status });
            x.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(y => y.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
