using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Application.Accounts;

/// <param name="Currency"><c>null</c> = the base currency.</param>
/// <param name="GoalAmount">Envelopes only; <c>null</c> = no goal.</param>
public sealed record AccountInput(
    string Name,
    AccountKind Kind = AccountKind.Regular,
    CurrencyCode? Currency = null,
    decimal OpeningBalance = 0,
    decimal? GoalAmount = null,
    DateOnly? GoalDate = null);

/// <summary>Creates and edits accounts and envelopes. Names are unique, case-insensitively.</summary>
public sealed class AccountService(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    ISettingsStore settings,
    DataChangedNotifier notifier)
{
    public async Task<Account> CreateAsync(AccountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var existing = await accounts.ListAsync(cancellationToken);
        EnsureUniqueName(existing, input.Name, exceptId: null);

        var sortOrder = existing.Count == 0 ? 0 : existing.Max(a => a.SortOrder) + 1;
        var account = new Account(
            Guid.CreateVersion7(),
            input.Name,
            input.Currency ?? settings.Load().BaseCurrency,
            sortOrder,
            input.Kind,
            input.OpeningBalance);
        ApplyGoal(account, input);

        accounts.Add(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        notifier.NotifyChanged();
        return account;
    }

    /// <summary>Renames, sets the opening balance and the goal. Kind and currency are fixed once transactions exist.</summary>
    public async Task<Account> UpdateAsync(Guid id, AccountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var account = await accounts.FindAsync(id, cancellationToken) ?? throw NotFound();
        EnsureUniqueName(await accounts.ListAsync(cancellationToken), input.Name, exceptId: id);

        if (input.Kind != account.Kind || (input.Currency is { } currency && currency != account.Currency))
        {
            throw new DomainException("Вид и валюту счёта нельзя поменять — создайте новый счёт и переведите на него деньги.");
        }

        account.Rename(input.Name);
        account.SetOpeningBalance(input.OpeningBalance);
        ApplyGoal(account, input);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        notifier.NotifyChanged();
        return account;
    }

    /// <summary>Hidden accounts keep their history but are not offered for new transactions.</summary>
    public async Task SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAsync(id, cancellationToken) ?? throw NotFound();
        if (hidden)
        {
            account.Hide();
        }
        else
        {
            account.Show();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        notifier.NotifyChanged();
    }

    public async Task<bool> HasTransactionsAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await transactions.ListAsync(cancellationToken: cancellationToken)).Any(t => t.AccountId == id || t.ToAccountId == id);

    private static void ApplyGoal(Account account, AccountInput input)
    {
        if (input.GoalAmount is { } goal)
        {
            account.SetGoal(goal, input.GoalDate);
        }
        else
        {
            account.ClearGoal();
        }
    }

    private static void EnsureUniqueName(IEnumerable<Account> existing, string name, Guid? exceptId)
    {
        if (existing.Any(a => a.Id != exceptId && string.Equals(a.Name, name?.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new DomainException($"Счёт или конверт «{name?.Trim()}» уже есть.");
        }
    }

    private static DomainException NotFound() => new("Счёт не найден.");
}
