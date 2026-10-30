using FinanceApp.Domain;

namespace FinanceApp.Application.ExchangeRates;

/// <summary>Official rates: rubles per one unit of each currency, as published on <see cref="PublishedDate"/>.</summary>
public sealed record RubRates(DateOnly PublishedDate, IReadOnlyDictionary<CurrencyCode, decimal> RubPerUnit);

/// <summary>A cached rate with the date it was requested for.</summary>
public sealed record CachedRubRate(DateOnly Date, CurrencyCode Currency, decimal RubPerUnit);

/// <summary>Remote source of official rates (the Central Bank of Russia).</summary>
public interface IExchangeRateSource
{
    /// <summary>Rates in force on <paramref name="date"/> (the last ones published on or before it).</summary>
    /// <exception cref="ExchangeRateSourceException">The source is unreachable or answered nonsense.</exception>
    Task<RubRates> GetRubRatesAsync(DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>Local store of rates so the app works offline and never asks twice for the same day.</summary>
public interface IExchangeRateCache
{
    Task<decimal?> FindAsync(DateOnly date, CurrencyCode currency, CancellationToken cancellationToken = default);

    /// <summary>The most recent cached rate on or before <paramref name="date"/>, or the most recent of all if none.</summary>
    Task<CachedRubRate?> FindLatestAsync(CurrencyCode currency, DateOnly date, CancellationToken cancellationToken = default);

    Task SaveAsync(DateOnly date, RubRates rates, CancellationToken cancellationToken = default);
}

/// <param name="IsFallback">Offline: the latest cached CBR rate, not the one of the date.</param>
/// <param name="IsManual">The user's own rate (<see cref="ManualRate"/>), alone or on one leg of the conversion.</param>
public sealed record ExchangeRateQuote(CurrencyCode From, CurrencyCode To, DateOnly Date, decimal Rate, bool IsFallback, bool IsManual = false);

public interface IExchangeRateService
{
    /// <summary>How many <paramref name="to"/> one <paramref name="from"/> is worth on <paramref name="date"/>.</summary>
    /// <exception cref="ExchangeRateUnavailableException">No rate online and none cached — the user has to enter it.</exception>
    Task<ExchangeRateQuote> GetRateAsync(CurrencyCode from, CurrencyCode to, DateOnly date, CancellationToken cancellationToken = default);
}

public sealed class ExchangeRateSourceException : Exception
{
    public ExchangeRateSourceException()
    {
    }

    public ExchangeRateSourceException(string message)
        : base(message)
    {
    }

    public ExchangeRateSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ExchangeRateUnavailableException : Exception
{
    public ExchangeRateUnavailableException()
    {
    }

    public ExchangeRateUnavailableException(string message)
        : base(message)
    {
    }

    public ExchangeRateUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
