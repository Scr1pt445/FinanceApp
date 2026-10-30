using FinanceApp.Application.Accounts;
using FinanceApp.Application.Debts;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Settings;
using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Presentation.Hosting;

/// <summary>
/// Each call is one unit of work: a fresh DI scope and database context. Shared by the desktop and the
/// Android app, whose view models live longer than any one database context should.
/// </summary>
public sealed class ScopedFinanceBackend(IServiceScopeFactory scopes) : IFinanceBackend
{
    public Task<BalancesOverview> LoadBalancesAsync(bool includeHidden = false, CancellationToken cancellationToken = default) =>
        InScopeAsync<AccountBalancesService, BalancesOverview>(s => s.LoadAsync(includeHidden, cancellationToken));

    public Task<PeriodOverview> LoadPeriodAsync(Period period, CancellationToken cancellationToken = default) =>
        InScopeAsync<PeriodOverviewService, PeriodOverview>(s => s.LoadAsync(period, cancellationToken));

    public Task<Catalog> LoadCatalogAsync(CancellationToken cancellationToken = default) =>
        InScopeAsync<CatalogService, Catalog>(s => s.LoadAsync(cancellationToken));

    public Task<SavedTransaction> AddAsync(TransactionInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<TransactionService, SavedTransaction>(s => s.AddAsync(input, cancellationToken));

    public Task<SavedTransaction> UpdateAsync(Guid id, TransactionInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<TransactionService, SavedTransaction>(s => s.UpdateAsync(id, input, cancellationToken));

    public Task<SavedTransaction> AddTransferAsync(TransferInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<TransactionService, SavedTransaction>(s => s.AddTransferAsync(input, cancellationToken));

    public Task<SavedTransaction> UpdateTransferAsync(Guid id, TransferInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<TransactionService, SavedTransaction>(s => s.UpdateTransferAsync(id, input, cancellationToken));

    public Task<Transaction> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        InScopeAsync<TransactionService, Transaction>(s => s.DeleteAsync(id, cancellationToken));

    public Task RestoreAsync(Transaction deleted, CancellationToken cancellationToken = default) =>
        InScopeAsync<TransactionService, bool>(async s =>
        {
            await s.RestoreAsync(deleted, cancellationToken);
            return true;
        });

    public Task<Account> CreateAccountAsync(AccountInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<AccountService, Account>(s => s.CreateAsync(input, cancellationToken));

    public Task<Account> UpdateAccountAsync(Guid id, AccountInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<AccountService, Account>(s => s.UpdateAsync(id, input, cancellationToken));

    public Task SetAccountHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default) =>
        InScopeAsync<AccountService, bool>(async s =>
        {
            await s.SetHiddenAsync(id, hidden, cancellationToken);
            return true;
        });

    public Task<Category> CreateCategoryAsync(string name, TransactionType type, CancellationToken cancellationToken = default) =>
        InScopeAsync<CategoryService, Category>(s => s.CreateAsync(name, type, cancellationToken));

    public Task<Category> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default) =>
        InScopeAsync<CategoryService, Category>(s => s.RenameAsync(id, name, cancellationToken));

    public Task SetCategoryHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default) =>
        InScopeAsync<CategoryService, bool>(async s =>
        {
            await s.SetHiddenAsync(id, hidden, cancellationToken);
            return true;
        });

    public Task<ExchangeRateQuote> GetRateAsync(CurrencyCode from, CurrencyCode to, DateOnly date, CancellationToken cancellationToken = default) =>
        InScopeAsync<IExchangeRateService, ExchangeRateQuote>(s => s.GetRateAsync(from, to, date, cancellationToken));

    public Task<IReadOnlyList<ManualRate>> ListManualRatesAsync(CancellationToken cancellationToken = default) =>
        InScopeAsync<ManualRateService, IReadOnlyList<ManualRate>>(s => Task.FromResult(s.List()));

    public Task<ManualRate> AddManualRateAsync(
        CurrencyCode from, CurrencyCode to, decimal rate, DateOnly validFrom, ManualRatePeriod period, CancellationToken cancellationToken = default) =>
        InScopeAsync<ManualRateService, ManualRate>(s => s.AddAsync(from, to, rate, validFrom, period, cancellationToken));

    public Task RemoveManualRateAsync(ManualRate rate, CancellationToken cancellationToken = default) =>
        InScopeAsync<ManualRateService, bool>(async s =>
        {
            await s.RemoveAsync(rate, cancellationToken);
            return true;
        });

    public Task<IReadOnlyList<RecurringOperation>> ListRecurringAsync(CancellationToken cancellationToken = default) =>
        InScopeAsync<RecurringService, IReadOnlyList<RecurringOperation>>(s => s.ListAsync(cancellationToken));

    public Task<RecurringOperation> AddRecurringAsync(RecurringInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<RecurringService, RecurringOperation>(s => s.AddAsync(input, cancellationToken));

    public Task<RecurringOperation> UpdateRecurringAsync(Guid id, RecurringInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<RecurringService, RecurringOperation>(s => s.UpdateAsync(id, input, cancellationToken));

    public Task DeleteRecurringAsync(Guid id, CancellationToken cancellationToken = default) =>
        InScopeAsync<RecurringService, bool>(async s =>
        {
            await s.DeleteAsync(id, cancellationToken);
            return true;
        });

    public Task<SavedTransaction> RecordRecurringAsync(Guid id, decimal? amount = null, CancellationToken cancellationToken = default) =>
        InScopeAsync<RecurringService, SavedTransaction>(s => s.RecordAsync(id, amount, cancellationToken));

    public Task SkipRecurringAsync(Guid id, CancellationToken cancellationToken = default) =>
        InScopeAsync<RecurringService, bool>(async s =>
        {
            await s.SkipAsync(id, cancellationToken);
            return true;
        });

    public Task<MonthLimits> LoadLimitsAsync(YearMonth month, CancellationToken cancellationToken = default) =>
        InScopeAsync<LimitsService, MonthLimits>(s => s.LoadAsync(month, cancellationToken));

    public Task SetCategoryLimitAsync(Guid id, decimal? limit, CancellationToken cancellationToken = default) =>
        InScopeAsync<CategoryService, bool>(async s =>
        {
            await s.SetLimitAsync(id, limit, cancellationToken);
            return true;
        });

    public Task<MonthComparison> LoadComparisonAsync(YearMonth month, YearMonth other, bool samePoint, CancellationToken cancellationToken = default) =>
        InScopeAsync<MonthComparisonService, MonthComparison>(s => s.LoadAsync(month, other, samePoint, cancellationToken));

    public Task<IReadOnlyList<Debt>> ListDebtsAsync(CancellationToken cancellationToken = default) =>
        InScopeAsync<DebtService, IReadOnlyList<Debt>>(s => s.ListAsync(cancellationToken));

    public Task<Debt> AddDebtAsync(DebtInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<DebtService, Debt>(s => s.AddAsync(input, cancellationToken));

    public Task<Debt> UpdateDebtAsync(Guid id, DebtInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<DebtService, Debt>(s => s.UpdateAsync(id, input, cancellationToken));

    public Task<Debt> RepayDebtAsync(Guid id, decimal amount, CancellationToken cancellationToken = default) =>
        InScopeAsync<DebtService, Debt>(s => s.RepayAsync(id, amount, cancellationToken));

    public Task DeleteDebtAsync(Guid id, CancellationToken cancellationToken = default) =>
        InScopeAsync<DebtService, bool>(async s =>
        {
            await s.DeleteAsync(id, cancellationToken);
            return true;
        });

    public Task SetCurrenciesAsync(IReadOnlyList<CurrencyCode> chosen, CancellationToken cancellationToken = default) =>
        InScopeAsync<CurrencySettingsService, bool>(async s =>
        {
            await s.SaveAsync(chosen, cancellationToken);
            return true;
        });

    public Task SetTotalsCurrencyAsync(CurrencyCode? currency, CancellationToken cancellationToken = default) =>
        InScopeAsync<ISettingsStore, bool>(async store =>
        {
            await store.SaveAsync(store.Load() with { TotalsCurrency = currency }, cancellationToken);
            return true;
        });

    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
