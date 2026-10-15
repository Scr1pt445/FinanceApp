using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Presentation.Main;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Desktop;

/// <summary>Each call is one unit of work: a fresh DI scope and database context.</summary>
internal sealed class ScopedMainBackend(IServiceScopeFactory scopes) : IMainBackend
{
    public Task<MonthOverview> LoadMonthAsync(YearMonth month, CancellationToken cancellationToken = default) =>
        InScopeAsync<MonthOverviewService, MonthOverview>(s => s.LoadAsync(month, cancellationToken));

    public Task<SavedTransaction> UpdateAsync(Guid id, TransactionInput input, CancellationToken cancellationToken = default) =>
        InScopeAsync<TransactionService, SavedTransaction>(s => s.UpdateAsync(id, input, cancellationToken));

    public Task<Transaction> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        InScopeAsync<TransactionService, Transaction>(s => s.DeleteAsync(id, cancellationToken));

    public Task RestoreAsync(Transaction deleted, CancellationToken cancellationToken = default) =>
        InScopeAsync<TransactionService, bool>(async s =>
        {
            await s.RestoreAsync(deleted, cancellationToken);
            return true;
        });

    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
