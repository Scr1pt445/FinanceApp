using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Application.ExchangeRates;

/// <summary>How long a rate typed by the user stays in force.</summary>
public enum ManualRatePeriod
{
    Day,
    Week,
    Month,

    /// <summary>Until the user removes it.</summary>
    UntilRemoved,
}

/// <summary>
/// A rate the user typed: «1 <see cref="From"/> = <see cref="Rate"/> <see cref="To"/>» from <see cref="ValidFrom"/>
/// to <see cref="ValidTo"/> inclusive (open-ended when <c>null</c>). It wins over the Central Bank's rate for those
/// days — abroad the bank's own rate is what counts, and offline there may be no CBR rate at all.
/// </summary>
public sealed record ManualRate(CurrencyCode From, CurrencyCode To, decimal Rate, DateOnly ValidFrom, DateOnly? ValidTo)
{
    public bool IsActiveOn(DateOnly date) => date >= ValidFrom && (ValidTo is null || date <= ValidTo);

    public bool IsExpired(DateOnly today) => ValidTo is { } end && end < today;

    /// <summary>Same two currencies, either way round.</summary>
    public bool Connects(CurrencyCode a, CurrencyCode b) => (From == a && To == b) || (From == b && To == a);

    /// <summary>How many <paramref name="to"/> one <paramref name="from"/> is worth by this rate; <c>null</c> for other currencies.</summary>
    public decimal? Convert(CurrencyCode from, CurrencyCode to) =>
        From == from && To == to ? Rate
        : From == to && To == from ? decimal.Round(1m / Rate, ExchangeRateService.RateDecimals, MidpointRounding.AwayFromZero)
        : null;

    /// <summary>The last day of a period that starts on <paramref name="start"/>.</summary>
    public static DateOnly? EndOf(DateOnly start, ManualRatePeriod period) => period switch
    {
        ManualRatePeriod.Day => start,
        ManualRatePeriod.Week => start.AddDays(6),
        ManualRatePeriod.Month => start.AddMonths(1).AddDays(-1),
        _ => null,
    };
}

/// <summary>The user's own rates, kept in the settings next to the database (not synced, like everything else).</summary>
public sealed class ManualRateService(ISettingsStore settings, TimeProvider timeProvider)
{
    /// <summary>Expired rates are kept this long, so the list still explains last week's totals.</summary>
    public const int KeepExpiredDays = 31;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public IReadOnlyList<ManualRate> List() => settings.Load().ManualRates;

    /// <summary>
    /// Adds a rate. Older rates of the same pair that the new one covers entirely are dropped, and so are rates that
    /// expired long ago; where periods still overlap, the newest rate wins.
    /// </summary>
    /// <exception cref="ArgumentException">The same currency twice, or a rate that is not positive.</exception>
    public async Task<ManualRate> AddAsync(
        CurrencyCode from, CurrencyCode to, decimal rate, DateOnly validFrom, ManualRatePeriod period, CancellationToken cancellationToken = default)
    {
        if (from == to)
        {
            throw new ArgumentException("Курс задаётся между двумя разными валютами.", nameof(to));
        }

        if (rate <= 0)
        {
            throw new ArgumentException("Курс — положительное число.", nameof(rate));
        }

        var added = new ManualRate(from, to, decimal.Round(rate, ExchangeRateService.RateDecimals, MidpointRounding.AwayFromZero), validFrom, ManualRate.EndOf(validFrom, period));
        var today = Today;
        var kept = List()
            .Where(r => !(r.Connects(from, to) && Covers(added, r)))
            .Where(r => !(r.ValidTo is { } end && end < today.AddDays(-KeepExpiredDays)))
            .Append(added)
            .ToList();
        await settings.SaveAsync(settings.Load() with { ManualRates = kept }, cancellationToken);
        return added;
    }

    public async Task RemoveAsync(ManualRate rate, CancellationToken cancellationToken = default)
    {
        var list = List().ToList();
        if (list.Remove(rate))
        {
            await settings.SaveAsync(settings.Load() with { ManualRates = list }, cancellationToken);
        }
    }

    private static bool Covers(ManualRate outer, ManualRate inner) =>
        outer.ValidFrom <= inner.ValidFrom
        && (outer.ValidTo is null || (inner.ValidTo is { } innerEnd && innerEnd <= outer.ValidTo));
}
