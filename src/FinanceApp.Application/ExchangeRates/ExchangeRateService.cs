using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceApp.Application.ExchangeRates;

/// <summary>
/// Rates via the ruble (ADR 0002): the user's own rate first, then the cache, the Central Bank and — offline —
/// the latest cached rate, flagged as a fallback so the UI can say so. Without any CBR rate, a pair is still
/// converted through the user's rates when they lead there (EUR → BYN typed, USD → EUR cached: USD → BYN).
/// </summary>
public sealed class ExchangeRateService(
    IExchangeRateSource source,
    IExchangeRateCache cache,
    TimeProvider timeProvider,
    ILogger<ExchangeRateService>? logger = null,
    ISettingsStore? settings = null) : IExchangeRateService
{
    /// <summary>Stored rates keep six decimal places — far more than kopeck precision needs.</summary>
    public const int RateDecimals = 6;

    private readonly ILogger _logger = logger ?? NullLogger<ExchangeRateService>.Instance;

    public async Task<ExchangeRateQuote> GetRateAsync(
        CurrencyCode from,
        CurrencyCode to,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (from == to)
        {
            return new ExchangeRateQuote(from, to, date, 1m, IsFallback: false);
        }

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var asOf = date > today ? today : date;

        // Of overlapping rates of one pair, the one added last wins.
        var manual = (settings?.Load().ManualRates ?? []).Where(r => r.IsActiveOn(asOf)).Reverse().ToList();
        if (Manual(manual, from, to) is { } own)
        {
            return new ExchangeRateQuote(from, to, date, own, IsFallback: false, IsManual: true);
        }

        try
        {
            return await CbrRateAsync(from, to, date, asOf, manual, cancellationToken);
        }
        catch (ExchangeRateUnavailableException) when (manual.Count > 0)
        {
            return await ThroughManualAsync(from, to, date, asOf, manual, cancellationToken)
                ?? throw new ExchangeRateUnavailableException(
                    $"Нет курса {from} → {to}: ЦБ недоступен, а своего курса для этой пары нет. Задайте его в «Курсах валют».");
        }
    }

    private static decimal? Manual(List<ManualRate> manual, CurrencyCode from, CurrencyCode to) =>
        manual.Select(r => r.Convert(from, to)).FirstOrDefault(r => r is not null);

    private async Task<ExchangeRateQuote> CbrRateAsync(
        CurrencyCode from, CurrencyCode to, DateOnly date, DateOnly asOf, List<ManualRate> manual, CancellationToken cancellationToken)
    {
        var (fromRub, fromFallback, fromManual) = await RubPerUnitAsync(from, asOf, manual, cancellationToken);
        var (toRub, toFallback, toManual) = await RubPerUnitAsync(to, asOf, manual, cancellationToken);

        var rate = decimal.Round(fromRub / toRub, RateDecimals, MidpointRounding.AwayFromZero);
        return new ExchangeRateQuote(from, to, date, rate, fromFallback || toFallback, fromManual || toManual);
    }

    /// <summary>A rate of the user's to some other currency, and from there on by the CBR (or another rate of the user's).</summary>
    private async Task<ExchangeRateQuote?> ThroughManualAsync(
        CurrencyCode from, CurrencyCode to, DateOnly date, DateOnly asOf, List<ManualRate> manual, CancellationToken cancellationToken)
    {
        var pivots = manual.SelectMany(r => new[] { r.From, r.To }).Distinct().Where(c => c != from && c != to);
        foreach (var pivot in pivots)
        {
            var first = Manual(manual, from, pivot);
            var second = Manual(manual, pivot, to);
            if (first is null && second is null)
            {
                continue;
            }

            try
            {
                var a = first ?? (await CbrRateAsync(from, pivot, date, asOf, manual, cancellationToken)).Rate;
                var b = second ?? (await CbrRateAsync(pivot, to, date, asOf, manual, cancellationToken)).Rate;
                var rate = decimal.Round(a * b, RateDecimals, MidpointRounding.AwayFromZero);
                return new ExchangeRateQuote(from, to, date, rate, IsFallback: false, IsManual: true);
            }
            catch (ExchangeRateUnavailableException)
            {
            }
        }

        return null;
    }

    private async Task<(decimal RubPerUnit, bool IsFallback, bool IsManual)> RubPerUnitAsync(
        CurrencyCode currency, DateOnly date, List<ManualRate> manual, CancellationToken cancellationToken)
    {
        if (currency != CurrencyCode.Rub && Manual(manual, currency, CurrencyCode.Rub) is { } own)
        {
            return (own, false, true);
        }

        var (rubPerUnit, fallback) = await GetRubPerUnitAsync(currency, date, cancellationToken);
        return (rubPerUnit, fallback, false);
    }

    private async Task<(decimal RubPerUnit, bool IsFallback)> GetRubPerUnitAsync(
        CurrencyCode currency,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (currency == CurrencyCode.Rub)
        {
            return (1m, false);
        }

        if (await cache.FindAsync(date, currency, cancellationToken) is { } cached)
        {
            return (cached, false);
        }

        RubRates rates;
        try
        {
            rates = await source.GetRubRatesAsync(date, cancellationToken);
        }
        catch (ExchangeRateSourceException ex)
        {
            _logger.LogWarning(ex, "Exchange rate source unavailable for {Date}; falling back to cache", date);
            var latest = await cache.FindLatestAsync(currency, date, cancellationToken);
            return latest is not null
                ? (latest.RubPerUnit, true)
                : throw new ExchangeRateUnavailableException(
                    $"Нет курса {currency} на {date:yyyy-MM-dd}: источник курсов недоступен, а в кэше курса нет. Введите курс вручную.",
                    ex);
        }

        await cache.SaveAsync(date, rates, cancellationToken);

        return rates.RubPerUnit.TryGetValue(currency, out var rubPerUnit)
            ? (rubPerUnit, false)
            : throw new ExchangeRateUnavailableException(
                $"ЦБ РФ не публикует курс {currency}. Введите курс вручную.");
    }
}
