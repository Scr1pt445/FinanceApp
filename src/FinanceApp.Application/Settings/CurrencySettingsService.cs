using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;

namespace FinanceApp.Application.Settings;

/// <summary>«Валюты»: which currencies the app offers besides the base one and those of the accounts.</summary>
public sealed class CurrencySettingsService(ISettingsStore settings, IAccountRepository accounts)
{
    /// <summary>
    /// Keeps <paramref name="chosen"/> in this order. The «Всего» totals go back to the base currency when theirs is no
    /// longer offered.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<CurrencyCode> chosen, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        var current = settings.Load();
        var list = chosen.Distinct().ToList();
        var offered = CurrencyList.Resolve(current.BaseCurrency, list, await accounts.ListAsync(cancellationToken));
        var totals = current.TotalsCurrency is { } shown && !offered.Contains(shown) ? null : current.TotalsCurrency;
        await settings.SaveAsync(current with { Currencies = list, TotalsCurrency = totals }, cancellationToken);
    }
}
