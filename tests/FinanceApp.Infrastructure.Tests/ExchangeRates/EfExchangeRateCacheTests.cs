using FinanceApp.Application.ExchangeRates;
using FinanceApp.Domain;
using FinanceApp.Infrastructure.ExchangeRates;

namespace FinanceApp.Infrastructure.Tests.ExchangeRates;

public class EfExchangeRateCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RubRates Rates(string published, decimal usd) =>
        new(DateOnly.Parse(published, System.Globalization.CultureInfo.InvariantCulture), new Dictionary<CurrencyCode, decimal> { [CurrencyCode.Usd] = usd });

    [Fact]
    public async Task SaveThenFind_ByRequestedDate()
    {
        await using var database = await TestDatabase.CreateAsync(seed: false);
        await using (var db = database.CreateContext())
        {
            await new EfExchangeRateCache(db, TimeProvider.System).SaveAsync(new DateOnly(2025, 10, 19), Rates("2025-10-18", 81.0548m), Ct);
        }

        await using var check = database.CreateContext();
        var cache = new EfExchangeRateCache(check, TimeProvider.System);
        (await cache.FindAsync(new DateOnly(2025, 10, 19), CurrencyCode.Usd, Ct)).ShouldBe(81.0548m);
        (await cache.FindAsync(new DateOnly(2025, 10, 18), CurrencyCode.Usd, Ct)).ShouldBeNull();
        (await cache.FindAsync(new DateOnly(2025, 10, 19), CurrencyCode.Eur, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Save_SameDateTwice_Overwrites()
    {
        await using var database = await TestDatabase.CreateAsync(seed: false);
        var date = new DateOnly(2025, 10, 19);
        await using (var db = database.CreateContext())
        {
            var cache = new EfExchangeRateCache(db, TimeProvider.System);
            await cache.SaveAsync(date, Rates("2025-10-18", 80m), Ct);
            await cache.SaveAsync(date, Rates("2025-10-18", 81m), Ct);
        }

        await using var check = database.CreateContext();
        (await new EfExchangeRateCache(check, TimeProvider.System).FindAsync(date, CurrencyCode.Usd, Ct)).ShouldBe(81m);
    }

    [Fact]
    public async Task FindLatest_PrefersOnOrBefore_ThenAnyLatest()
    {
        await using var database = await TestDatabase.CreateAsync(seed: false);
        await using var db = database.CreateContext();
        var cache = new EfExchangeRateCache(db, TimeProvider.System);
        await cache.SaveAsync(new DateOnly(2025, 10, 1), Rates("2025-10-01", 80m), Ct);
        await cache.SaveAsync(new DateOnly(2025, 10, 10), Rates("2025-10-10", 81m), Ct);
        await cache.SaveAsync(new DateOnly(2025, 10, 20), Rates("2025-10-20", 82m), Ct);

        (await cache.FindLatestAsync(CurrencyCode.Usd, new DateOnly(2025, 10, 15), Ct))!.RubPerUnit.ShouldBe(81m);
        (await cache.FindLatestAsync(CurrencyCode.Usd, new DateOnly(2025, 9, 1), Ct))!.RubPerUnit.ShouldBe(82m);
        (await cache.FindLatestAsync(CurrencyCode.Eur, new DateOnly(2025, 10, 15), Ct)).ShouldBeNull();
    }
}
