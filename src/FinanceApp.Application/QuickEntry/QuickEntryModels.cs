using FinanceApp.Domain;

namespace FinanceApp.Application.QuickEntry;

/// <summary>Everything the parser needs besides the input line. Hidden categories and accounts are ignored.</summary>
/// <param name="LastAccountByType">Account of the most recent transaction of each type.</param>
/// <param name="BaseCurrency">Only accounts in this currency are used by default; others need an explicit <c>@account</c>.</param>
public sealed record QuickEntryContext(
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Account> Accounts,
    IReadOnlyDictionary<TransactionType, Guid> LastAccountByType,
    CurrencyCode BaseCurrency,
    CategoryHints Hints,
    DateOnly Today);

public enum QuickEntryIssueKind
{
    EmptyInput,
    InvalidAmount,
    MissingCategory,
    AmbiguousCategory,
    UnknownAccount,
    AmbiguousAccount,
    NoAccount,
}

/// <summary>Why the line cannot be saved yet. <see cref="Message"/> is user-facing (Russian).</summary>
public sealed record QuickEntryIssue(QuickEntryIssueKind Kind, string Message);

/// <summary>
/// What the line means so far. Shown live under the input box; saved only when <see cref="IsComplete"/>.
/// </summary>
public sealed record QuickEntryDraft(
    TransactionType Type,
    decimal? Amount,
    Category? Category,
    bool CategoryFromHint,
    Account? Account,
    bool AccountIsDefault,
    DateOnly Date,
    string? Note,
    IReadOnlyList<QuickEntryIssue> Issues)
{
    public bool IsComplete => Issues.Count == 0 && Amount is not null && Category is not null && Account is not null;
}
