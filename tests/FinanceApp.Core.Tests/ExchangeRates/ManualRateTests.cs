using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Core.Tests.ExchangeRates;

/// <summary>The user's own rates: for a trip abroad, or when the CBR cannot be reached.</summary>
public class ManualRateTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly CurrencyCode Byn = CurrencyCode.Byn;

    private readonly MemorySettings _settings = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly OfflineSource _source = new();
    private readonly EmptyCache _cache = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ExchangeRateService Rates() => new(_source, _cache, _time, settings: _settings);

    private ManualRateService Manual() => new(_settings, _time);

    [Theory]
    [InlineData(ManualRatePeriod.Day, "2026-10-15")]
    [InlineData(ManualRatePeriod.Week, "2026-10-21")]
    [InlineData(ManualRatePeriod.Month, "2026-11-14")]
    public void Periods_EndOnTheirLastDay(ManualRatePeriod period, string end) =>
        ManualRate.EndOf(Today, period).ShouldBe(DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void UntilRemoved_HasNoEnd() => ManualRate.EndOf(Today, ManualRatePeriod.UntilRemoved).ShouldBeNull();

    [Fact]
    public async Task Offline_AManualRateGivesTheTotals()
    {
        await Manual().AddAsync(CurrencyCode.Eur, Byn, 3.45m, Today, ManualRatePeriod.Week, Ct);

        var quote = await Rates().GetRateAsync(CurrencyCode.Eur, Byn, Today, Ct);
        var back = await Rates().GetRateAsync(Byn, CurrencyCode.Eur, Today.AddDays(6), Ct);

        quote.Rate.ShouldBe(3.45m);
        quote.IsManual.ShouldBeTrue();
        back.Rate.ShouldBe(0.289855m);
    }

    [Fact]
    public async Task OutsideItsPeriod_TheRateIsNotUsed()
    {
        await Manual().AddAsync(CurrencyCode.Eur, Byn, 3.45m, Today.AddDays(-10), ManualRatePeriod.Day, Ct);

        await Should.ThrowAsync<ExchangeRateUnavailableException>(() => Rates().GetRateAsync(CurrencyCode.Eur, Byn, Today, Ct));
    }

    [Fact]
    public async Task AManualRate_WinsOverTheCbr()
    {
        _source.Online = new Dictionary<CurrencyCode, decimal> { [CurrencyCode.Usd] = 90m, [CurrencyCode.Eur] = 100m, [Byn] = 29m };
        await Manual().AddAsync(CurrencyCode.Eur, Byn, 3.3m, Today, ManualRatePeriod.UntilRemoved, Ct);

        (await Rates().GetRateAsync(CurrencyCode.Eur, Byn, Today, Ct)).Rate.ShouldBe(3.3m);
        (await Rates().GetRateAsync(CurrencyCode.Usd, CurrencyCode.Rub, Today, Ct)).IsManual.ShouldBeFalse();
    }

    [Fact]
    public async Task ARateToTheRuble_IsOneLegOfACrossRate()
    {
        _source.Online = new Dictionary<CurrencyCode, decimal> { [Byn] = 30m };
        await Manual().AddAsync(CurrencyCode.Eur, CurrencyCode.Rub, 99m, Today, ManualRatePeriod.Week, Ct);

        var quote = await Rates().GetRateAsync(CurrencyCode.Eur, Byn, Today, Ct);

        quote.Rate.ShouldBe(3.3m);
        quote.IsManual.ShouldBeTrue();
    }

    [Fact]
    public async Task Offline_TwoManualRates_ChainThroughTheirCommonCurrency()
    {
        await Manual().AddAsync(CurrencyCode.Usd, CurrencyCode.Eur, 0.9m, Today, ManualRatePeriod.Week, Ct);
        await Manual().AddAsync(CurrencyCode.Eur, Byn, 3.5m, Today, ManualRatePeriod.Week, Ct);

        (await Rates().GetRateAsync(CurrencyCode.Usd, Byn, Today, Ct)).Rate.ShouldBe(3.15m);
    }

    [Fact]
    public async Task ANewerRate_ReplacesTheOneItCovers_AndWinsWhereTheyOverlap()
    {
        var manual = Manual();
        await manual.AddAsync(CurrencyCode.Eur, Byn, 3.4m, Today, ManualRatePeriod.Day, Ct);
        await manual.AddAsync(CurrencyCode.Eur, Byn, 3.5m, Today.AddDays(-1), ManualRatePeriod.Month, Ct);
        manual.List().ShouldHaveSingleItem().Rate.ShouldBe(3.5m);

        await manual.AddAsync(Byn, CurrencyCode.Eur, 0.25m, Today, ManualRatePeriod.Day, Ct);

        manual.List().Count.ShouldBe(2);
        (await Rates().GetRateAsync(CurrencyCode.Eur, Byn, Today, Ct)).Rate.ShouldBe(4m);
        (await Rates().GetRateAsync(CurrencyCode.Eur, Byn, Today.AddDays(-1), Ct)).Rate.ShouldBe(3.5m);
    }

    [Fact]
    public async Task LongExpiredRates_AreDropped_RecentOnesKept()
    {
        var manual = Manual();
        await manual.AddAsync(CurrencyCode.Usd, Byn, 3m, Today.AddDays(-60), ManualRatePeriod.Day, Ct);
        await manual.AddAsync(CurrencyCode.Usd, Byn, 3.1m, Today.AddDays(-5), ManualRatePeriod.Day, Ct);

        await manual.AddAsync(CurrencyCode.Eur, Byn, 3.5m, Today, ManualRatePeriod.Day, Ct);

        manual.List().Select(r => r.Rate).ShouldBe([3.1m, 3.5m]);
    }

    [Fact]
    public async Task SameCurrencyTwice_IsRefused() =>
        await Should.ThrowAsync<ArgumentException>(() => Manual().AddAsync(Byn, Byn, 1m, Today, ManualRatePeriod.Day, Ct));

    [Fact]
    public async Task Remove_ForgetsTheRate()
    {
        var manual = Manual();
        var rate = await manual.AddAsync(CurrencyCode.Eur, Byn, 3.4m, Today, ManualRatePeriod.Day, Ct);

        await manual.RemoveAsync(rate, Ct);

        manual.List().ShouldBeEmpty();
    }

    [Fact]
    public void Describe_SaysWhenTheRateApplies()
    {
        var week = ManualRatesViewModel.Describe(new ManualRate(CurrencyCode.Eur, Byn, 3.45m, Today, Today.AddDays(6)), Today);
        var day = ManualRatesViewModel.Describe(new ManualRate(CurrencyCode.Eur, Byn, 3.45m, Today.AddDays(-3), Today.AddDays(-3)), Today);
        var open = ManualRatesViewModel.Describe(new ManualRate(CurrencyCode.Usd, Byn, 3m, Today, null), Today);

        week.Title.ShouldBe("1 EUR = 3,45 BYN");
        week.PeriodText.ShouldBe("15–21 октября 2026");
        week.IsActive.ShouldBeTrue();
        day.PeriodText.ShouldBe("только 12 октября 2026 · истёк");
        day.IsExpired.ShouldBeTrue();
        open.PeriodText.ShouldBe("с 15 октября 2026, пока не уберёте");
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private AppSettings _current = new();

        public AppSettings Load() => _current;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class OfflineSource : IExchangeRateSource
    {
        public Dictionary<CurrencyCode, decimal>? Online { get; set; }

        public Task<RubRates> GetRubRatesAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            Online is { } rates
                ? Task.FromResult(new RubRates(date, rates))
                : throw new ExchangeRateSourceException("offline");
    }

    private sealed class EmptyCache : IExchangeRateCache
    {
        public Task<decimal?> FindAsync(DateOnly date, CurrencyCode currency, CancellationToken cancellationToken = default) => Task.FromResult<decimal?>(null);

        public Task<CachedRubRate?> FindLatestAsync(CurrencyCode currency, DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<CachedRubRate?>(null);

        public Task SaveAsync(DateOnly date, RubRates rates, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
