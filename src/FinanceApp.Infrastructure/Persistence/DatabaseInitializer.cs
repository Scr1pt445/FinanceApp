using FinanceApp.Application.Setup;
using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure.Persistence;

/// <summary>Brings the schema up to date and fills an empty database with the starter catalog.</summary>
public sealed class DatabaseInitializer(FinanceDbContext db)
{
    /// <returns><c>true</c> if the starter catalog was created.</returns>
    public async Task<bool> InitializeAsync(CurrencyCode baseCurrency, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        if (await db.Categories.AnyAsync(cancellationToken) || await db.Accounts.AnyAsync(cancellationToken))
        {
            return false;
        }

        db.Categories.AddRange(DefaultCatalog.CreateCategories());
        db.Accounts.AddRange(DefaultCatalog.CreateAccounts(baseCurrency));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
