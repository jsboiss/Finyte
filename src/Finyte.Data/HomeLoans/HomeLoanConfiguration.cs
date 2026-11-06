using Finyte.Core.Accounts;
using Finyte.Core.HomeLoans;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.HomeLoans;

public static class HomeLoanConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HomeLoanProfile>(x =>
        {
            x.ToTable("home_loan_profiles");
            x.HasKey(y => y.Id);
            x.Property(y => y.FirstApplicant).HasMaxLength(60).IsRequired();
            x.Property(y => y.SecondApplicant).HasMaxLength(60);
            x.Property(y => y.SpendingAccountIds).HasColumnType("uuid[]");
            x.Property(y => y.SavingsAccountIds).HasColumnType("uuid[]");
            x.Property(y => y.Version).IsConcurrencyToken();
            x.HasIndex(y => y.TenantId).IsUnique();
        });
        modelBuilder.Entity<HomeLoanIncomeSource>(x =>
        {
            x.ToTable("home_loan_income_sources");
            x.HasKey(y => y.Id);
            x.Property(y => y.MatchKey).HasMaxLength(200).IsRequired();
            x.Property(y => y.Name).HasMaxLength(256).IsRequired();
            x.Property(y => y.Kind).HasMaxLength(24).IsRequired();
            x.HasIndex(y => new { y.TenantId, y.MatchKey }).IsUnique();
        });
        modelBuilder.Entity<MerchantVendor>(x =>
        {
            x.ToTable("merchant_vendors");
            x.HasKey(y => new { y.TenantId, y.MatchKey });
            x.Property(y => y.MatchKey).HasMaxLength(256);
            x.Property(y => y.Vendor).HasMaxLength(120).IsRequired();
        });
        modelBuilder.Entity<HomeLoanCategoryChoice>(x =>
        {
            x.ToTable("home_loan_category_choices");
            x.HasKey(y => new { y.TenantId, y.TagId });
            x.Property(y => y.Category).HasMaxLength(40).IsRequired();
            x.HasOne<TransactionTag>().WithMany().HasForeignKey(y => y.TagId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
