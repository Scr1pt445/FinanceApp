using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Application.Accounts;

/// <summary>What a transaction form offers: categories (most used first), accounts and sensible defaults.</summary>
/// <param name="CategoryUsage">How many transactions use each category — frequent categories go first.</param>
/// <param name="LastAccountByType">Account of the most recent transaction of each type, for defaults.</param>
public sealed record Catalog(
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Account> Accounts,
    IReadOnlyDictionary<Guid, int> CategoryUsage,
    IReadOnlyDictionary<TransactionType, Guid> LastAccountByType,
    CurrencyCode BaseCurrency)
{
    /// <summary>Visible categories of the type, most used first, then in catalog order.</summary>
    public IReadOnlyList<Category> CategoriesFor(TransactionType type) =>
        Categories
            .Where(c => c.Type == type && !c.IsHidden)
            .OrderByDescending(c => CategoryUsage.GetValueOrDefault(c.Id))
            .ThenBy(c => c.SortOrder)
            .ToList();

    /// <summary>The last regular account used for the type, else the first visible regular one.</summary>
    public Account? DefaultAccount(TransactionType type)
    {
        var regular = Accounts.Where(a => !a.IsHidden && !a.IsEnvelope).ToList();
        return regular.FirstOrDefault(a => LastAccountByType.TryGetValue(type, out var last) && a.Id == last)
            ?? regular.FirstOrDefault(a => a.Currency == BaseCurrency)
            ?? regular.FirstOrDefault();
    }
}

public sealed class CatalogService(
    ICategoryRepository categories,
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ISettingsStore settings)
{
    public async Task<Catalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        var history = await transactions.ListAsync(cancellationToken: cancellationToken);
        var allAccounts = await accounts.ListAsync(cancellationToken);
        var envelopes = allAccounts.Where(a => a.IsEnvelope).Select(a => a.Id).ToHashSet();

        var usage = history
            .Where(t => t.CategoryId is not null)
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // Only regular accounts become defaults: spending from an envelope is always a deliberate choice.
        var last = history
            .Where(t => !envelopes.Contains(t.AccountId))
            .GroupBy(t => t.Type)
            .ToDictionary(g => g.Key, g => g.MaxBy(t => t.CreatedAt)!.AccountId);

        return new Catalog(
            await categories.ListAsync(cancellationToken),
            allAccounts,
            usage,
            last,
            settings.Load().BaseCurrency);
    }
}
