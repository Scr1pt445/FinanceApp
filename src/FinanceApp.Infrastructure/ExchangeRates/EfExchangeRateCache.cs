using FinanceApp.Application.ExchangeRates;
using FinanceApp.Domain;
using FinanceApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure.ExchangeRates;

internal sealed class EfExchangeRateCache(FinanceDbContext db, TimeProvider timeProvider) : IExchangeRateCache
{
    public async Task<decimal?> FindAsync(DateOnly date, CurrencyCode currency, CancellationToken cancellationToken = default) =>
        await db.ExchangeRates.AsNoTracking()
            .Where(r => r.Date == date && r.Currency == currency)
            .Select(r => (decimal?)r.RubPerUnit)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<CachedRubRate?> FindLatestAsync(CurrencyCode currency, DateOnly date, CancellationToken cancellationToken = default)
    {
        var rates = db.ExchangeRates.AsNoTracking().Where(r => r.Currency == currency);
        var latest = await rates.Where(r => r.Date <= date).OrderByDescending(r => r.Date).FirstOrDefaultAsync(cancellationToken)
            ?? await rates.OrderByDescending(r => r.Date).FirstOrDefaultAsync(cancellationToken);

        return latest is null ? null : new CachedRubRate(latest.Date, latest.Currency, latest.RubPerUnit);
    }

    public async Task SaveAsync(DateOnly date, RubRates rates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rates);

        var existing = await db.ExchangeRates.Where(r => r.Date == date).ToDictionaryAsync(r => r.Currency, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var (currency, rubPerUnit) in rates.RubPerUnit)
        {
            if (!existing.TryGetValue(currency, out var row))
            {
                row = new CachedExchangeRate { Date = date, Currency = currency };
                db.ExchangeRates.Add(row);
            }

            row.RubPerUnit = rubPerUnit;
            row.PublishedDate = rates.PublishedDate;
            row.FetchedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
