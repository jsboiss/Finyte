using Finyte.Core.Recurring;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Recurring;

public static class RecurringPaymentConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RecurringPaymentSeries>(x =>
        {
            x.ToTable("recurring_payment_series");
            x.HasKey(y => y.Id);
            x.Property(y => y.Name).HasMaxLength(120);
            x.Property(y => y.Currency).HasMaxLength(3);
            x.Property(y => y.Cadence).HasMaxLength(16);
            x.Property(y => y.AmountMode).HasMaxLength(16);
            x.Property(y => y.State).HasMaxLength(16);
            x.Property(y => y.ExpectedAmount).HasPrecision(18, 2);
            x.Property(y => y.Version).IsConcurrencyToken();
            x.HasIndex(y => new { y.TenantId, y.Name });
            x.HasMany(y => y.Aliases).WithOne().HasForeignKey(y => y.SeriesId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RecurringPaymentAlias>(x =>
        {
            x.ToTable("recurring_payment_aliases");
            x.HasKey(y => y.Id);
            x.Property(y => y.Field).HasMaxLength(16);
            x.Property(y => y.Value).HasMaxLength(512);
            x.Property(y => y.NormalizedValue).HasMaxLength(512);
            x.HasIndex(y => new { y.SeriesId, y.Field, y.NormalizedValue }).IsUnique();
        });
        modelBuilder.Entity<RecurringPaymentDecision>(x =>
        {
            x.ToTable("recurring_payment_decisions");
            x.HasKey(y => y.Id);
            x.Property(y => y.Status).HasMaxLength(16);
            x.Property(y => y.Fingerprint).HasMaxLength(64);
            x.Property(y => y.SnapshotJson).HasColumnType("jsonb");
            x.HasOne<RecurringPaymentSeries>().WithMany().HasForeignKey(y => y.SeriesId).OnDelete(DeleteBehavior.Cascade);
            x.HasIndex(y => new { y.TenantId, y.SeriesId, y.TransactionId, y.OccurrenceDate }).IsUnique();
            x.HasIndex(y => new { y.TenantId, y.TransactionId }).IsUnique().HasFilter("\"Status\" = 'confirmed'");
            x.HasIndex(y => new { y.SeriesId, y.OccurrenceDate }).IsUnique().HasFilter("\"Status\" = 'confirmed'");
        });
        modelBuilder.Entity<RecurringPaymentReview>(x =>
        {
            x.ToTable("recurring_payment_reviews");
            x.HasKey(y => y.Id);
            x.Property(y => y.Action).HasMaxLength(16);
            x.Property(y => y.SnapshotJson).HasColumnType("jsonb");
            x.Property(y => y.ReviewedByUserId).HasMaxLength(128);
            x.HasOne<RecurringPaymentSeries>().WithMany().HasForeignKey(y => y.SeriesId).OnDelete(DeleteBehavior.Cascade);
            x.HasIndex(y => new { y.TenantId, y.SeriesId, y.ReviewedAt });
        });
        modelBuilder.Entity<RecurringDiscoveryDecision>(x =>
        {
            x.ToTable("recurring_discovery_decisions");
            x.HasKey(y => new { y.TenantId, y.CandidateKey });
            x.Property(y => y.CandidateKey).HasMaxLength(256);
        });
    }
}
