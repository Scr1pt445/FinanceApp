using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Settings;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Application.QuickEntry;

/// <summary>The line cannot be saved yet; <see cref="Issues"/> say why.</summary>
public sealed class QuickEntryException : Exception
{
    public QuickEntryException(IReadOnlyList<QuickEntryIssue> issues)
        : base(string.Join(" ", issues.Select(i => i.Message)))
    {
        Issues = issues;
    }

    public QuickEntryException()
    {
        Issues = [];
    }

    public QuickEntryException(string message)
        : base(message)
    {
        Issues = [];
    }

    public QuickEntryException(string message, Exception innerException)
        : base(message, innerException)
    {
        Issues = [];
    }

    public IReadOnlyList<QuickEntryIssue> Issues { get; }
}

/// <summary>
/// Quick entry use case: load the context once when the window opens, preview on every keystroke,
/// save on Enter through <see cref="TransactionService"/> (so rates and the CSV export work as usual).
/// </summary>
public sealed class QuickEntryService(
    ICategoryRepository categories,
    IAccountRepository accounts,
    ITransactionRepository transactions,
    TransactionService transactionService,
    ISettingsStore settings,
    TimeProvider timeProvider)
{
    public async Task<QuickEntryContext> LoadContextAsync(CancellationToken cancellationToken = default)
    {
        var history = await transactions.ListAsync(cancellationToken: cancellationToken);
        var baseCurrency = settings.Load().BaseCurrency;

        // Foreign-currency transactions do not count: after a dinner abroad the default goes back to
        // the last base-currency account of that type, not to the first one in the list.
        var lastAccountByType = history
            .Where(t => t.Currency == baseCurrency)
            .GroupBy(t => t.Type)
            .ToDictionary(g => g.Key, g => g.MaxBy(t => t.CreatedAt)!.AccountId);

        return new QuickEntryContext(
            await categories.ListAsync(cancellationToken),
            await accounts.ListAsync(cancellationToken),
            lastAccountByType,
            baseCurrency,
            CategoryHints.Build(history),
            DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));
    }

    /// <summary>Parses with a context loaded earlier — cheap enough for every keystroke.</summary>
    public static QuickEntryDraft Preview(string? input, QuickEntryContext context) => QuickEntryParser.Parse(input, context);

    /// <exception cref="QuickEntryException">The line is incomplete or ambiguous.</exception>
    public async Task<SavedTransaction> SaveAsync(string input, CancellationToken cancellationToken = default)
    {
        var draft = QuickEntryParser.Parse(input, await LoadContextAsync(cancellationToken));
        if (!draft.IsComplete)
        {
            throw new QuickEntryException(draft.Issues.Count > 0
                ? draft.Issues
                : [new QuickEntryIssue(QuickEntryIssueKind.MissingCategory, "Строка неполная.")]);
        }

        return await transactionService.AddAsync(
            new TransactionInput(draft.Date, draft.Type, draft.Amount!.Value, draft.Category!.Id, draft.Account!.Id, draft.Note),
            cancellationToken);
    }
}
