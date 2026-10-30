using FinanceApp.Application.Abstractions;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Application.Accounts;

/// <param name="Balance">In the account currency.</param>
/// <param name="BaseBalance">In the base currency at today's rate; <c>null</c> if no rate is available.</param>
public sealed record AccountBalance(Account Account, decimal Balance, decimal? BaseBalance)
{
    /// <summary>0..1 towards the envelope goal; <c>null</c> without a goal.</summary>
    public decimal? GoalProgress =>
        Account.GoalAmount is { } goal ? Math.Clamp(Balance / goal, 0, 1) : null;

    /// <summary>
    /// How much to put aside per month to reach the goal by its date, counting the current month;
    /// <c>null</c> without a goal date or once the goal is reached.
    /// </summary>
    public decimal? MonthlyToGoal(DateOnly today)
    {
        if (Account is not { GoalAmount: { } goal, GoalDate: { } date } || Balance >= goal)
        {
            return null;
        }

        var months = Math.Max(((date.Year - today.Year) * 12) + date.Month - today.Month + 1, 1);
        return decimal.Round((goal - Balance) / months, 0, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Left column of the main window: accounts, envelopes and the totals above them.</summary>
/// <param name="FreeTotal">On regular accounts, in the base currency.</param>
/// <param name="EnvelopesTotal">In envelopes, in the base currency.</param>
/// <param name="DisplayCurrency">The currency the user wants totals in (settings), or the base one.</param>
/// <param name="DisplayRate">Base → display currency at today's rate; 1 for the base currency.</param>
public sealed record BalancesOverview(
    IReadOnlyList<AccountBalance> Accounts,
    IReadOnlyList<AccountBalance> Envelopes,
    decimal FreeTotal,
    decimal EnvelopesTotal,
    CurrencyCode BaseCurrency,
    bool HasMissingRates,
    CurrencyCode DisplayCurrency,
    decimal DisplayRate = 1m,
    IReadOnlyList<CurrencyCode>? ChosenCurrencies = null)
{
    public decimal Total => FreeTotal + EnvelopesTotal;

    /// <summary>A base-currency total converted to <see cref="DisplayCurrency"/>.</summary>
    public decimal InDisplay(decimal baseAmount) => decimal.Round(baseAmount * DisplayRate, 2, MidpointRounding.AwayFromZero);

    /// <summary>Currencies worth offering for the totals: the base one, the user's and those of the accounts.</summary>
    public IReadOnlyList<CurrencyCode> DisplayChoices =>
        CurrencyList.Resolve(BaseCurrency, ChosenCurrencies, Accounts.Concat(Envelopes).Select(b => b.Account));
}

/// <summary>
/// Balances = opening balance + effect of every transaction (ADR 0010). Totals convert foreign accounts
/// at today's rate; without a rate the account is left out of the totals and the overview says so.
/// </summary>
public sealed class AccountBalancesService(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IExchangeRateService exchangeRates,
    ISettingsStore settings,
    TimeProvider timeProvider)
{
    public async Task<BalancesOverview> LoadAsync(bool includeHidden = false, CancellationToken cancellationToken = default)
    {
        var all = await accounts.ListAsync(cancellationToken);
        var history = await transactions.ListAsync(cancellationToken: cancellationToken);
        var baseCurrency = settings.Load().BaseCurrency;
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

        var balances = new List<AccountBalance>();
        var missingRates = false;
        foreach (var account in all.Where(a => includeHidden || !a.IsHidden))
        {
            var balance = account.OpeningBalance + history.Sum(t => t.EffectOn(account.Id));
            decimal? baseBalance = balance;
            if (account.Currency != baseCurrency)
            {
                try
                {
                    var quote = await exchangeRates.GetRateAsync(account.Currency, baseCurrency, today, cancellationToken);
                    baseBalance = decimal.Round(balance * quote.Rate, 2, MidpointRounding.AwayFromZero);
                }
                catch (ExchangeRateUnavailableException)
                {
                    baseBalance = null;
                    missingRates = true;
                }
            }

            balances.Add(new AccountBalance(account, balance, baseBalance));
        }

        // Totals in another currency: one more rate; offline without a cached rate — back to the base currency.
        var display = settings.Load().TotalsCurrency ?? baseCurrency;
        var displayRate = 1m;
        if (display != baseCurrency)
        {
            try
            {
                displayRate = (await exchangeRates.GetRateAsync(baseCurrency, display, today, cancellationToken)).Rate;
            }
            catch (ExchangeRateUnavailableException)
            {
                display = baseCurrency;
                missingRates = true;
            }
        }

        var regular = balances.Where(b => !b.Account.IsEnvelope).ToList();
        var envelopes = balances.Where(b => b.Account.IsEnvelope).ToList();
        return new BalancesOverview(
            regular,
            envelopes,
            regular.Sum(b => b.BaseBalance ?? 0),
            envelopes.Sum(b => b.BaseBalance ?? 0),
            baseCurrency,
            missingRates,
            display,
            displayRate,
            settings.Load().Currencies);
    }
}
