using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Transactions;

namespace FinanceApp.Presentation.QuickEntry;

/// <summary>
/// What the quick-entry window needs from the core. The desktop app implements it with a fresh
/// DI scope per call, tests with a fake.
/// </summary>
public interface IQuickEntryBackend
{
    Task<QuickEntryContext> LoadContextAsync(CancellationToken cancellationToken = default);

    /// <exception cref="QuickEntryException">The line is incomplete.</exception>
    Task<SavedTransaction> SaveAsync(string line, CancellationToken cancellationToken = default);
}
