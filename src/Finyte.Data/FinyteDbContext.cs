using Finyte.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data;

public class FinyteDbContext(DbContextOptions<FinyteDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(x =>
        {
            x.ToTable("accounts");

            x.HasKey(y => y.Id);

            x.Property(y => y.UserId)
                .HasMaxLength(128)
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

            x.HasIndex(y => y.UserId);
            x.HasIndex(y => new { y.UserId, y.FiskilAccountId })
                .IsUnique()
                .HasFilter("\"FiskilAccountId\" IS NOT NULL");
        });

        modelBuilder.Entity<Transaction>(x =>
        {
            x.ToTable("transactions");

            x.HasKey(y => y.Id);

            x.Property(y => y.UserId)
                .HasMaxLength(128)
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

            x.HasIndex(y => y.UserId);
            x.HasIndex(y => y.AccountId);
            x.HasIndex(y => new { y.UserId, y.FiskilTransactionId })
                .IsUnique();
            x.HasIndex(y => y.PostedAt);
        });
    }
}
