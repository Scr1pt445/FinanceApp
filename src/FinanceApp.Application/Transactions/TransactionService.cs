using FinanceApp.Application.Abstractions;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceApp.Application.Transactions;

/// <param name="ExchangeRate">Manual rate to the base currency; <c>null</c> = look it up.</param>
public sealed record TransactionInput(
    DateOnly Date,
    TransactionType Type,
    decimal Amount,
    Guid CategoryId,
    Guid AccountId,
    string? Note,
    decimal? ExchangeRate = null);

/// <param name="RateIsFallback">The rate is the latest cached one because the rate source was offline.</param>
public sealed record SavedTransaction(Transaction Transaction, bool RateIsFallback);

/// <summary>Adds, edits and deletes transactions; notifies integrations after every commit.</summary>
public sealed class TransactionService(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    IExchangeRateService exchangeRates,
    ISettingsStore settings,
    TimeProvider timeProvider,
    IEnumerable<IDataChangedListener> listeners,
    ILogger<TransactionService>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<TransactionService>.Instance;

    public async Task<SavedTransaction> AddAsync(TransactionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var (category, account) = await LoadReferencesAsync(input, cancellationToken);
        var (rate, isFallback) = await ResolveRateAsync(input, account, cancellationToken);

        var transaction = Transaction.Create(
            input.Date, input.Type, input.Amount, rate, category, account, input.Note, Now);
        transactions.Add(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        NotifyChanged();
        return new SavedTransaction(transaction, isFallback);
    }

    public async Task<SavedTransaction> UpdateAsync(Guid id, TransactionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var transaction = await transactions.FindAsync(id, cancellationToken) ?? throw NotFound(id);
        var (category, account) = await LoadReferencesAsync(input, cancellationToken);

        // Keep the stored (possibly hand-edited) rate unless what it depends on has changed.
        var rateStillValid = input.ExchangeRate is null && transaction.Date == input.Date && transaction.AccountId == account.Id;
        var (rate, isFallback) = rateStillValid
            ? (transaction.ExchangeRate, false)
            : await ResolveRateAsync(input, account, cancellationToken);

        transaction.Update(input.Date, input.Type, input.Amount, rate, category, account, input.Note, Now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        NotifyChanged();
        return new SavedTransaction(transaction, isFallback);
    }

    /// <returns>The deleted transaction, so the caller can offer undo.</returns>
    public async Task<Transaction> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await transactions.FindAsync(id, cancellationToken) ?? throw NotFound(id);
        transactions.Remove(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        NotifyChanged();
        return transaction;
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<(Category Category, Account Account)> LoadReferencesAsync(TransactionInput input, CancellationToken cancellationToken)
    {
        var category = await categories.FindAsync(input.CategoryId, cancellationToken)
            ?? throw new DomainException("Категория не найдена.");
        var account = await accounts.FindAsync(input.AccountId, cancellationToken)
            ?? throw new DomainException("Счёт не найден.");
        return (category, account);
    }

    private async Task<(decimal Rate, bool IsFallback)> ResolveRateAsync(TransactionInput input, Account account, CancellationToken cancellationToken)
    {
        if (input.ExchangeRate is { } manual)
        {
            return (manual, false);
        }

        var baseCurrency = settings.Load().BaseCurrency;
        if (account.Currency == baseCurrency)
        {
            return (1m, false);
        }

        var quote = await exchangeRates.GetRateAsync(account.Currency, baseCurrency, input.Date, cancellationToken);
        return (quote.Rate, quote.IsFallback);
    }

    private void NotifyChanged()
    {
        foreach (var listener in listeners)
        {
            try
            {
                listener.OnDataChanged();
            }
#pragma warning disable CA1031 // An integration must never break saving a transaction.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Data-changed listener {Listener} failed", listener.GetType().Name);
            }
        }
    }

    private static DomainException NotFound(Guid id) => new($"Операция {id} не найдена.");
}
