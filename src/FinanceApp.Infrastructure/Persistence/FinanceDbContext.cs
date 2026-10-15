using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinanceApp.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<CachedExchangeRate> ExchangeRates => Set<CachedExchangeRate>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<CurrencyCode>().HaveConversion<CurrencyCodeConverter>().HaveMaxLength(3);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<TransactionType>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<AccountKind>().HaveConversion<string>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(category =>
        {
            category.ToTable("Categories");
            category.HasKey(c => c.Id);
            category.Property(c => c.Id).ValueGeneratedNever();
            category.Property(c => c.Name).HasMaxLength(Category.MaxNameLength);
            category.HasIndex(c => new { c.Name, c.Type }).IsUnique();
        });

        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable("Accounts");
            account.HasKey(a => a.Id);
            account.Property(a => a.Id).ValueGeneratedNever();
            account.Property(a => a.Name).HasMaxLength(Account.MaxNameLength);
            account.Property(a => a.Kind).HasDefaultValue(AccountKind.Regular).HasSentinel((AccountKind)0);
            account.Property(a => a.OpeningBalance).HasDefaultValue(0m);
            account.HasIndex(a => a.Name).IsUnique();
        });

        modelBuilder.Entity<Transaction>(transaction =>
        {
            transaction.ToTable("Transactions");
            transaction.HasKey(t => t.Id);
            transaction.Property(t => t.Id).ValueGeneratedNever();
            transaction.Property(t => t.Note).HasMaxLength(Transaction.MaxNoteLength);
            transaction.Ignore(t => t.BaseAmount);
            transaction.HasIndex(t => t.Date);

            // Categories and accounts are hidden, never deleted, while transactions reference them.
            transaction.HasOne<Category>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
            transaction.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
            transaction.HasOne<Account>().WithMany().HasForeignKey(t => t.ToAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CachedExchangeRate>(rate =>
        {
            rate.ToTable("ExchangeRates");
            rate.HasKey(r => new { r.Date, r.Currency });
        });
    }

    private sealed class CurrencyCodeConverter() : ValueConverter<CurrencyCode, string>(
        code => code.Value,
        value => CurrencyCode.Parse(value));

    /// <summary>SQLite returns <see cref="DateTimeKind.Unspecified"/>; all stored timestamps are UTC.</summary>
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
