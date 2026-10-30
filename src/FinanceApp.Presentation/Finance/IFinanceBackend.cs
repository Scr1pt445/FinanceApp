using FinanceApp.Application.Accounts;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>Everything the main window needs from the core; each call is one unit of work.</summary>
public interface IFinanceBackend
{
    Task<BalancesOverview> LoadBalancesAsync(bool includeHidden = false, CancellationToken cancellationToken = default);

    Task<PeriodOverview> LoadPeriodAsync(Period period, CancellationToken cancellationToken = default);

    Task<Catalog> LoadCatalogAsync(CancellationToken cancellationToken = default);

    Task<SavedTransaction> AddAsync(TransactionInput input, CancellationToken cancellationToken = default);

    Task<SavedTransaction> UpdateAsync(Guid id, TransactionInput input, CancellationToken cancellationToken = default);

    Task<SavedTransaction> AddTransferAsync(TransferInput input, CancellationToken cancellationToken = default);

    Task<SavedTransaction> UpdateTransferAsync(Guid id, TransferInput input, CancellationToken cancellationToken = default);

    Task<Transaction> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task RestoreAsync(Transaction deleted, CancellationToken cancellationToken = default);

    Task<Account> CreateAccountAsync(AccountInput input, CancellationToken cancellationToken = default);

    Task<Account> UpdateAccountAsync(Guid id, AccountInput input, CancellationToken cancellationToken = default);

    Task SetAccountHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default);

    Task<Category> CreateCategoryAsync(string name, TransactionType type, CancellationToken cancellationToken = default);

    Task<Category> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default);

    Task SetCategoryHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default);

    /// <summary>How many <paramref name="to"/> one <paramref name="from"/> is worth on the date (CBR, cached).</summary>
    /// <exception cref="ExchangeRateUnavailableException">No rate online and none cached.</exception>
    Task<ExchangeRateQuote> GetRateAsync(CurrencyCode from, CurrencyCode to, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>The user's own rates (ManualRateService), the expired ones included.</summary>
    Task<IReadOnlyList<ManualRate>> ListManualRatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a rate of the user's for a day, a week, a month or until removed; it wins over the CBR rate.</summary>
    Task<ManualRate> AddManualRateAsync(
        CurrencyCode from, CurrencyCode to, decimal rate, DateOnly validFrom, ManualRatePeriod period, CancellationToken cancellationToken = default);

    Task RemoveManualRateAsync(ManualRate rate, CancellationToken cancellationToken = default);

    /// <summary>Recurring operations, the soonest due first.</summary>
    Task<IReadOnlyList<RecurringOperation>> ListRecurringAsync(CancellationToken cancellationToken = default);

    Task<RecurringOperation> AddRecurringAsync(RecurringInput input, CancellationToken cancellationToken = default);

    Task<RecurringOperation> UpdateRecurringAsync(Guid id, RecurringInput input, CancellationToken cancellationToken = default);

    Task DeleteRecurringAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Records a due recurring operation for its day (with another amount this time, if given) and moves it on.</summary>
    Task<SavedTransaction> RecordRecurringAsync(Guid id, decimal? amount = null, CancellationToken cancellationToken = default);

    Task SkipRecurringAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Monthly limits of expense categories and what is spent of them in <paramref name="month"/>.</summary>
    Task<MonthLimits> LoadLimitsAsync(YearMonth month, CancellationToken cancellationToken = default);

    /// <summary>Sets or removes (<c>null</c>) a category's monthly limit, in the base currency.</summary>
    Task SetCategoryLimitAsync(Guid id, decimal? limit, CancellationToken cancellationToken = default);

    /// <summary>Remembers the currency of the «Всего» totals; <c>null</c> — the base currency.</summary>
    Task SetTotalsCurrencyAsync(CurrencyCode? currency, CancellationToken cancellationToken = default);
}
