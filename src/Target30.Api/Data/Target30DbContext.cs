using Microsoft.EntityFrameworkCore;
using Target30.Api.Models;

namespace Target30.Api.Data;

public class Target30DbContext(DbContextOptions<Target30DbContext> options) : DbContext(options)
{
    public DbSet<PlaidItem> PlaidItems => Set<PlaidItem>();
    public DbSet<PlaidTransaction> PlaidTransactions => Set<PlaidTransaction>();
    public DbSet<PlaidAccount> PlaidAccounts => Set<PlaidAccount>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<RecurringBill> RecurringBills => Set<RecurringBill>();
    public DbSet<CardBalanceSnapshot> CardBalanceSnapshots => Set<CardBalanceSnapshot>();
    public DbSet<CategoryBudget> CategoryBudgets => Set<CategoryBudget>();
    public DbSet<RecurringIncome> RecurringIncomes => Set<RecurringIncome>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<CardRewardRate> CardRewardRates => Set<CardRewardRate>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Access token do Plaid criptografado no banco (ver TokenCrypto).
        modelBuilder.Entity<PlaidItem>()
            .Property(i => i.AccessToken)
            .HasConversion(v => TokenCrypto.Protect(v), v => TokenCrypto.Unprotect(v));

        modelBuilder.Entity<RecurringBill>()
            .HasIndex(b => b.UserId);

        modelBuilder.Entity<Subscription>()
            .HasKey(s => s.UserId);

        modelBuilder.Entity<Subscription>()
            .HasIndex(s => s.StripeCustomerId);

        modelBuilder.Entity<CardRewardRate>()
            .HasIndex(r => new { r.UserId, r.AccountId, r.Category })
            .IsUnique();

        modelBuilder.Entity<CategoryRule>()
            .HasIndex(r => new { r.UserId, r.MerchantKey })
            .IsUnique();

        modelBuilder.Entity<RecurringIncome>()
            .HasIndex(i => i.UserId);

        modelBuilder.Entity<PlaidTransaction>()
            .HasIndex(t => t.PlaidTransactionId)
            .IsUnique();

        modelBuilder.Entity<PlaidTransaction>()
            .HasIndex(t => t.UserId);

        modelBuilder.Entity<PlaidAccount>()
            .HasIndex(a => a.AccountId)
            .IsUnique();

        modelBuilder.Entity<PlaidAccount>()
            .HasIndex(a => a.UserId);

        modelBuilder.Entity<UserSettings>()
            .HasKey(s => s.UserId);

        modelBuilder.Entity<CardBalanceSnapshot>()
            .HasIndex(s => new { s.AccountId, s.Date })
            .IsUnique();

        modelBuilder.Entity<CategoryBudget>()
            .HasIndex(b => new { b.UserId, b.Category })
            .IsUnique();
    }
}
