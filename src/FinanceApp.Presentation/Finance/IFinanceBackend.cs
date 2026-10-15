using FinanceApp.Application.Accounts;
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
}
