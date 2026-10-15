using FinanceApp.Application.Accounts;
using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Desktop;

/// <summary>Each call is one unit of work: a fresh DI scope and database context.</summary>
internal sealed class ScopedFinanceBackend(IServiceScopeFactory scopes) : IFinanceBackend
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

    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
