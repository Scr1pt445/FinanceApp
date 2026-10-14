using FinanceApp.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceApp.Application.ExchangeRates;

/// <summary>
/// Rates via the ruble (ADR 0002): cache first, then the Central Bank, then — offline — the latest
/// cached rate, flagged as a fallback so the UI can say so.
/// </summary>
public sealed class ExchangeRateService(
    IExchangeRateSource source,
    IExchangeRateCache cache,
    TimeProvider timeProvider,
    ILogger<ExchangeRateService>? logger = null) : IExchangeRateService
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

        var (fromRub, fromFallback) = await GetRubPerUnitAsync(from, asOf, cancellationToken);
        var (toRub, toFallback) = await GetRubPerUnitAsync(to, asOf, cancellationToken);

        var rate = decimal.Round(fromRub / toRub, RateDecimals, MidpointRounding.AwayFromZero);
        return new ExchangeRateQuote(from, to, date, rate, fromFallback || toFallback);
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
