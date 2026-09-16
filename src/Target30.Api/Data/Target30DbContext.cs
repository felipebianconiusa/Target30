using Microsoft.EntityFrameworkCore;
using Target30.Api.Models;

namespace Target30.Api.Data;

public class Target30DbContext(DbContextOptions<Target30DbContext> options) : DbContext(options)
{
    public DbSet<PlaidItem> PlaidItems => Set<PlaidItem>();
    public DbSet<PlaidTransaction> PlaidTransactions => Set<PlaidTransaction>();
    public DbSet<PlaidAccount> PlaidAccounts => Set<PlaidAccount>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    }
}
