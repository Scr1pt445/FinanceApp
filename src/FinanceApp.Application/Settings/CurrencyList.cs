using FinanceApp.Domain;

namespace FinanceApp.Application.Settings;

/// <summary>
/// The currencies the app offers in its choices (rates, debts, the receipt currency, the totals): the base one, those
/// the user picked in «Валюты» and those of the visible accounts — an account in dollars brings dollars in on its own.
/// </summary>
public static class CurrencyList
{
    /// <summary>What is offered until the user changes the list.</summary>
    public static IReadOnlyList<CurrencyCode> Defaults { get; } = [CurrencyCode.Rub, CurrencyCode.Usd, CurrencyCode.Eur];

    /// <param name="chosen">The user's list; <c>null</c> — never changed, <see cref="Defaults"/>.</param>
    public static IReadOnlyList<CurrencyCode> Resolve(CurrencyCode baseCurrency, IReadOnlyList<CurrencyCode>? chosen, IEnumerable<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return new[] { baseCurrency }
            .Concat(chosen ?? Defaults)
            .Concat(accounts.Where(a => !a.IsHidden).Select(a => a.Currency))
            .Distinct()
            .ToList();
    }
}
