using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Transactions;
using FinanceApp.Presentation.QuickEntry;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Presentation.Hosting;

/// <summary>
/// Each call is one unit of work: a fresh DI scope and database context. Shared by the desktop and the
/// Android app, whose view models live longer than any one database context should.
/// </summary>
public sealed class ScopedQuickEntryBackend(IServiceScopeFactory scopes) : IQuickEntryBackend
{
    public async Task<QuickEntryContext> LoadContextAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<QuickEntryService>().LoadContextAsync(cancellationToken);
    }

    public async Task<SavedTransaction> SaveAsync(string line, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<QuickEntryService>().SaveAsync(line, cancellationToken);
    }
}
