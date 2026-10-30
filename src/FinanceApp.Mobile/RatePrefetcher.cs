using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Mobile;

/// <summary>
/// Today's CBR rates, fetched once a day in the background — on start and when the app comes back — so the
/// screens find them in the cache instead of waiting for the network.
/// </summary>
public sealed class RatePrefetcher(IServiceScopeFactory scopes, ISettingsStore settings, TimeProvider timeProvider)
{
    private DateOnly? _loadedFor;

    /// <returns><c>true</c> if fresh rates arrived and the screens are worth refreshing.</returns>
    public async Task<bool> WarmUpAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        if (_loadedFor == today)
        {
            return false;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var rates = scope.ServiceProvider.GetRequiredService<IExchangeRateService>();
            var baseCurrency = settings.Load().BaseCurrency;

            // Asking for one currency stores the whole day's table in the cache.
            var other = baseCurrency == CurrencyCode.Usd ? CurrencyCode.Eur : CurrencyCode.Usd;
            var quote = await rates.GetRateAsync(other, baseCurrency, today, cancellationToken);
            if (quote.IsFallback)
            {
                return false;
            }

            _loadedFor = today;
            return true;
        }
        catch (ExchangeRateUnavailableException)
        {
            return false;
        }
    }
}
