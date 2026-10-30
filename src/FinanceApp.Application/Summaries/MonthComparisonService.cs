using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Accounts;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Application.Summaries;

/// <summary>Loads what <see cref="MonthComparison"/> needs in one go.</summary>
public sealed class MonthComparisonService(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IAccountRepository accounts,
    IRecurringOperationRepository recurring,
    LimitsService limits,
    IExchangeRateService rates,
    ISettingsStore settings,
    TimeProvider timeProvider)
{
    public async Task<MonthComparison> LoadAsync(YearMonth month, YearMonth other, bool samePoint, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var from = new[] { month.Previous.FirstDay, other.FirstDay }.Min();
        var to = new[] { month.LastDay, other.LastDay }.Max();
        var loaded = await transactions.ListAsync(from, to, cancellationToken);
        var categoryMap = (await categories.ListAsync(cancellationToken)).ToDictionary(c => c.Id);
        var accountList = await accounts.ListAsync(cancellationToken);
        var baseCurrency = settings.Load().BaseCurrency;

        IReadOnlyList<RecurringOperation> planned = [];
        MonthLimits? monthLimits = null;
        var known = new Dictionary<CurrencyCode, decimal?> { [baseCurrency] = 1m };
        if (month == YearMonth.Of(today))
        {
            planned = await recurring.ListAsync(cancellationToken);
            monthLimits = await limits.LoadAsync(month, cancellationToken);

            // Recurring amounts are in their accounts' currencies; a currency without a rate is left out of the forecast.
            foreach (var currency in planned.Select(p => accountList.FirstOrDefault(a => a.Id == p.AccountId)?.Currency).OfType<CurrencyCode>().Distinct())
            {
                if (!known.ContainsKey(currency))
                {
                    try
                    {
                        known[currency] = (await rates.GetRateAsync(currency, baseCurrency, today, cancellationToken)).Rate;
                    }
                    catch (ExchangeRateUnavailableException)
                    {
                        known[currency] = null;
                    }
                }
            }
        }

        return MonthComparison.For(
            loaded,
            month,
            other,
            samePoint,
            categoryMap,
            accountList.ToDictionary(a => a.Id),
            planned,
            monthLimits,
            currency => known.GetValueOrDefault(currency),
            baseCurrency,
            today);
    }
}
