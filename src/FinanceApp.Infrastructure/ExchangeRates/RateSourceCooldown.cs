using FinanceApp.Application.ExchangeRates;

namespace FinanceApp.Infrastructure.ExchangeRates;

/// <summary>
/// Remembers that the rate source just failed. Without it every screen refresh offline waited for the HTTP
/// timeout again before falling back to cached rates — the app felt stuck on every action.
/// </summary>
public sealed class RateSourceCooldown(TimeProvider timeProvider)
{
    /// <summary>How long to trust the cache alone after a failure before asking the network again.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMinutes(5);

    private DateTimeOffset? _failedAt;

    public bool IsCoolingDown => _failedAt is { } failed && timeProvider.GetUtcNow() - failed < Pause;

    public void Failed() => _failedAt = timeProvider.GetUtcNow();

    public void Succeeded() => _failedAt = null;
}

/// <summary>The CBR source behind <see cref="RateSourceCooldown"/>: right after a failure it fails at once.</summary>
public sealed class CoolingExchangeRateSource(CbrExchangeRateSource inner, RateSourceCooldown cooldown) : IExchangeRateSource
{
    public async Task<RubRates> GetRubRatesAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        if (cooldown.IsCoolingDown)
        {
            throw new ExchangeRateSourceException("Нет связи с ЦБ — используем последний известный курс.");
        }

        try
        {
            var rates = await inner.GetRubRatesAsync(date, cancellationToken);
            cooldown.Succeeded();
            return rates;
        }
        catch (ExchangeRateSourceException)
        {
            cooldown.Failed();
            throw;
        }
    }
}
