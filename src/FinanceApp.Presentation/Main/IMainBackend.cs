using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Main;

/// <summary>What the main window needs from the core; one unit of work per call.</summary>
public interface IMainBackend
{
    Task<PeriodOverview> LoadMonthAsync(YearMonth month, CancellationToken cancellationToken = default);

    Task<SavedTransaction> UpdateAsync(Guid id, TransactionInput input, CancellationToken cancellationToken = default);

    Task<Transaction> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task RestoreAsync(Transaction deleted, CancellationToken cancellationToken = default);
}
