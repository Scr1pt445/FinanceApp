using FinanceApp.Application.ExchangeRates;
using FinanceApp.Domain;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Core.Tests.ExchangeRates;

public class ExchangeRateServiceTests
{
    private static readonly DateOnly Oct7 = new(2026, 10, 7);

    private readonly FakeSource _source = new();
    private readonly InMemoryCache _cache = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    private ExchangeRateService CreateService() => new(_source, _cache, _time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SameCurrency_IsOne_WithoutCallingSource()
    {
        var quote = await CreateService().GetRateAsync(CurrencyCode.Usd, CurrencyCode.Usd, Oct7, Ct);

        quote.Rate.ShouldBe(1m);
        _source.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task UsdToRub_FetchesFromSourceAndCaches()
    {
        _source.Rates[Oct7] = new RubRates(Oct7, new Dictionary<CurrencyCode, decimal> { [CurrencyCode.Usd] = 92.4567m });
        var service = CreateService();

        var first = await service.GetRateAsync(CurrencyCode.Usd, CurrencyCode.Rub, Oct7, Ct);
        var second = await service.GetRateAsync(CurrencyCode.Usd, CurrencyCode.Rub, Oct7, Ct);

        first.Rate.ShouldBe(92.4567m);
        first.IsFallback.ShouldBeFalse();
        second.Rate.ShouldBe(92.4567m);
        _source.Calls.ShouldBe([Oct7]);
    }

    [Fact]
    public async Task CrossRate_GoesThroughRuble()
    {
        _source.Rates[Oct7] = new RubRates(Oct7, new Dictionary<CurrencyCode, decimal>
        {
            [CurrencyCode.Usd] = 90m,
            [CurrencyCode.Eur] = 100m,
            [CurrencyCode.Byn] = 27.5m,
        });
        var service = CreateService();

        (await service.GetRateAsync(CurrencyCode.Usd, CurrencyCode.Eur, Oct7, Ct)).Rate.ShouldBe(0.9m);
        (await service.GetRateAsync(CurrencyCode.Rub, CurrencyCode.Byn, Oct7, Ct)).Rate.ShouldBe(0.036364m);
    }

    [Fact]
    public async Task FutureDate_UsesToday()
    {
        var today = new DateOnly(2026, 10, 15);
        _source.Rates[today] = new RubRates(today, new Dictionary<CurrencyCode, decimal> { [CurrencyCode.Usd] = 95m });

        var quote = await CreateService().GetRateAsync(CurrencyCode.Usd, CurrencyCode.Rub, today.AddDays(10), Ct);

        quote.Rate.ShouldBe(95m);
        _source.Calls.ShouldBe([today]);
    }

    [Fact]
    public async Task Offline_FallsBackToLatestCachedRate()
    {
        _cache.Stored.Add(new CachedRubRate(new DateOnly(2026, 10, 1), CurrencyCode.Usd, 91m));
        _cache.Stored.Add(new CachedRubRate(new DateOnly(2026, 10, 5), CurrencyCode.Usd, 92m));
        _cache.Stored.Add(new CachedRubRate(new DateOnly(2026, 10, 9), CurrencyCode.Usd, 93m));
        _source.Offline = true;

        var quote = await CreateService().GetRateAsync(CurrencyCode.Usd, CurrencyCode.Rub, Oct7, Ct);

        quote.Rate.ShouldBe(92m);
        quote.IsFallback.ShouldBeTrue();
    }

    [Fact]
    public async Task Offline_WithEmptyCache_AsksForManualRate()
    {
        _source.Offline = true;

        var error = await Should.ThrowAsync<ExchangeRateUnavailableException>(
            () => CreateService().GetRateAsync(CurrencyCode.Usd, CurrencyCode.Rub, Oct7, Ct));

        error.Message.ShouldContain("вручную");
    }

    [Fact]
    public async Task UnknownCurrency_AsksForManualRate()
    {
        _source.Rates[Oct7] = new RubRates(Oct7, new Dictionary<CurrencyCode, decimal> { [CurrencyCode.Usd] = 90m });

        await Should.ThrowAsync<ExchangeRateUnavailableException>(
            () => CreateService().GetRateAsync(CurrencyCode.Parse("XYZ"), CurrencyCode.Rub, Oct7, Ct));
    }

    private sealed class FakeSource : IExchangeRateSource
    {
        public Dictionary<DateOnly, RubRates> Rates { get; } = [];

        public List<DateOnly> Calls { get; } = [];

        public bool Offline { get; set; }

        public Task<RubRates> GetRubRatesAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            Calls.Add(date);
            if (Offline)
            {
                throw new ExchangeRateSourceException("offline");
            }

            return Task.FromResult(Rates.TryGetValue(date, out var rates)
                ? rates
                : new RubRates(date, new Dictionary<CurrencyCode, decimal>()));
        }
    }

    private sealed class InMemoryCache : IExchangeRateCache
    {
        public List<CachedRubRate> Stored { get; } = [];

        public Task<decimal?> FindAsync(DateOnly date, CurrencyCode currency, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.FirstOrDefault(r => r.Date == date && r.Currency == currency)?.RubPerUnit);

        public Task<CachedRubRate?> FindLatestAsync(CurrencyCode currency, DateOnly date, CancellationToken cancellationToken = default)
        {
            var ofCurrency = Stored.Where(r => r.Currency == currency).OrderByDescending(r => r.Date).ToList();
            return Task.FromResult(ofCurrency.FirstOrDefault(r => r.Date <= date) ?? ofCurrency.FirstOrDefault());
        }

        public Task SaveAsync(DateOnly date, RubRates rates, CancellationToken cancellationToken = default)
        {
            Stored.AddRange(rates.RubPerUnit.Select(pair => new CachedRubRate(date, pair.Key, pair.Value)));
            return Task.CompletedTask;
        }
    }
}
