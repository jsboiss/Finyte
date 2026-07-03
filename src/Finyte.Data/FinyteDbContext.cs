using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Billing;
using Finyte.Core.ProviderSync;
using Finyte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data;

public class FinyteDbContext(DbContextOptions<FinyteDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<OverviewProjection> OverviewProjections => Set<OverviewProjection>();

    public DbSet<ProjectionState> ProjectionStates => Set<ProjectionState>();

    public DbSet<BillingCustomer> BillingCustomers => Set<BillingCustomer>();

    public DbSet<BillingSubscription> BillingSubscriptions => Set<BillingSubscription>();

    public DbSet<BillingEvent> BillingEvents => Set<BillingEvent>();

    public DbSet<ProviderConnection> ProviderConnections => Set<ProviderConnection>();

    public DbSet<ProviderWebhookEvent> ProviderWebhookEvents => Set<ProviderWebhookEvent>();

    public DbSet<ProviderSyncRun> ProviderSyncRuns => Set<ProviderSyncRun>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantMember> TenantMembers => Set<TenantMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(x =>
        {
            x.ToTable("accounts");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

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

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => y.AccountId);
            x.HasIndex(y => new { y.TenantId, y.FiskilTransactionId })
                .IsUnique();
            x.HasIndex(y => y.PostedAt);
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
        });

        modelBuilder.Entity<ProjectionState>(x =>
        {
            x.ToTable("projection_states");

            x.HasKey(y => y.Id);

            x.Property(y => y.TenantId)
                .IsRequired();

            x.Property(y => y.ProjectionKey)
                .HasMaxLength(80)
                .IsRequired();

            x.Property(y => y.ScopeKey)
                .HasMaxLength(256)
                .IsRequired();

            x.Property(y => y.ScopeJson)
                .HasColumnType("jsonb")
                .IsRequired();

            x.Property(y => y.Status)
                .HasMaxLength(32)
                .IsRequired();

            x.Property(y => y.IsStale)
                .IsRequired();

            x.Property(y => y.StaleReason)
                .HasMaxLength(512);

            x.Property(y => y.LastError)
                .HasMaxLength(2048);

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.Property(y => y.UpdatedAt)
                .IsRequired();

            x.HasIndex(y => new { y.TenantId, y.ProjectionKey, y.ScopeKey })
                .IsUnique();
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

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.Property(y => y.UpdatedAt)
                .IsRequired();

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => new { y.Provider, y.EndUserId })
                .IsUnique();
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

            x.Property(y => y.CreatedAt)
                .IsRequired();

            x.HasIndex(y => y.TenantId);
            x.HasIndex(y => new { y.TenantId, y.Status });
            x.HasIndex(y => y.ExternalMessageId);
        });

        modelBuilder.Entity<Tenant>(x =>
        {
            x.ToTable("tenants");

            x.HasKey(y => y.Id);

            x.Property(y => y.Name)
                .HasMaxLength(160)
                .IsRequired();

            x.Property(y => y.CreatedAt)
                .IsRequired();
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
            x.HasIndex(y => y.UserId)
                .IsUnique();
        });
    }
}
