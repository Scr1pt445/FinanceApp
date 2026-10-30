using FinanceApp.Application.Abstractions;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Application.Transactions;

/// <param name="ExchangeRate">Manual rate to the base currency; <c>null</c> = look it up.</param>
/// <param name="PaidCurrency">
/// The currency of the receipt, when it is not the account's — a card in rubles paying euros abroad.
/// <see cref="Amount"/> is then in it and is converted into the account currency; the note keeps the receipt amount.
/// </param>
/// <param name="PaidRate">Manual rate: account currency per one unit of <see cref="PaidCurrency"/> (what the bank charged); <c>null</c> = the CBR rate of the date.</param>
public sealed record TransactionInput(
    DateOnly Date,
    TransactionType Type,
    decimal Amount,
    Guid CategoryId,
    Guid AccountId,
    string? Note,
    decimal? ExchangeRate = null,
    CurrencyCode? PaidCurrency = null,
    decimal? PaidRate = null);

/// <param name="ToAmount">Received amount for accounts in different currencies; <c>null</c> = convert at the CBR rate.</param>
/// <param name="ExchangeRate">Manual rate of the source currency to the base currency; <c>null</c> = look it up.</param>
public sealed record TransferInput(
    DateOnly Date,
    decimal Amount,
    Guid FromAccountId,
    Guid ToAccountId,
    string? Note,
    decimal? ToAmount = null,
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
    DataChangedNotifier notifier)
{
    public async Task<SavedTransaction> AddAsync(TransactionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var (category, account) = await LoadReferencesAsync(input, cancellationToken);
        (input, var paidFallback) = await ConvertPaidAsync(input, account, cancellationToken);
        var (rate, isFallback) = await ResolveRateAsync(input, account, cancellationToken);

        var transaction = Transaction.Create(
            input.Date, input.Type, input.Amount, rate, category, account, input.Note, Now);
        transactions.Add(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        notifier.NotifyChanged();
        return new SavedTransaction(transaction, isFallback || paidFallback);
    }

    public async Task<SavedTransaction> UpdateAsync(Guid id, TransactionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var transaction = await transactions.FindAsync(id, cancellationToken) ?? throw NotFound(id);
        var (category, account) = await LoadReferencesAsync(input, cancellationToken);
        (input, var paidFallback) = await ConvertPaidAsync(input, account, cancellationToken);

        // Keep the stored (possibly hand-edited) rate unless what it depends on has changed.
        var rateStillValid = input.ExchangeRate is null && transaction.Date == input.Date && transaction.AccountId == account.Id;
        var (rate, isFallback) = rateStillValid
            ? (transaction.ExchangeRate, false)
            : await ResolveRateAsync(input, account, cancellationToken);

        transaction.Update(input.Date, input.Type, input.Amount, rate, category, account, input.Note, Now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        notifier.NotifyChanged();
        return new SavedTransaction(transaction, isFallback || paidFallback);
    }

    /// <summary>Moves money between accounts or envelopes (ADR 0010).</summary>
    public async Task<SavedTransaction> AddTransferAsync(TransferInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var (from, to) = await LoadAccountsAsync(input, cancellationToken);
        var (rate, rateFallback) = await ResolveRateAsync(input.ExchangeRate, from, input.Date, cancellationToken);
        var (toAmount, toFallback) = await ResolveToAmountAsync(input, from, to, cancellationToken);

        var transaction = Transaction.CreateTransfer(input.Date, input.Amount, rate, from, to, toAmount, input.Note, Now);
        transactions.Add(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        notifier.NotifyChanged();
        return new SavedTransaction(transaction, rateFallback || toFallback);
    }

    public async Task<SavedTransaction> UpdateTransferAsync(Guid id, TransferInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var transaction = await transactions.FindAsync(id, cancellationToken) ?? throw NotFound(id);
        var (from, to) = await LoadAccountsAsync(input, cancellationToken);

        var rateStillValid = input.ExchangeRate is null && transaction.Date == input.Date && transaction.AccountId == from.Id;
        var (rate, rateFallback) = rateStillValid
            ? (transaction.ExchangeRate, false)
            : await ResolveRateAsync(input.ExchangeRate, from, input.Date, cancellationToken);
        var (toAmount, toFallback) = await ResolveToAmountAsync(input, from, to, cancellationToken);

        transaction.UpdateTransfer(input.Date, input.Amount, rate, from, to, toAmount, input.Note, Now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        notifier.NotifyChanged();
        return new SavedTransaction(transaction, rateFallback || toFallback);
    }

    /// <returns>The deleted transaction, so the caller can offer undo.</returns>
    public async Task<Transaction> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await transactions.FindAsync(id, cancellationToken) ?? throw NotFound(id);
        transactions.Remove(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        notifier.NotifyChanged();
        return transaction;
    }

    /// <summary>
    /// Undo of <see cref="DeleteAsync"/>: puts the very same transaction back (same id, creation time and rate),
    /// so the CSV export and any reference to the id stay as they were.
    /// </summary>
    public async Task RestoreAsync(Transaction deleted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deleted);

        if ((await transactions.ExistingIdsAsync([deleted.Id], cancellationToken)).Count > 0)
        {
            return;
        }

        if (deleted.CategoryId is { } categoryId)
        {
            _ = await categories.FindAsync(categoryId, cancellationToken)
                ?? throw new DomainException("Категория операции больше не существует.");
        }

        foreach (var accountId in new[] { deleted.AccountId, deleted.ToAccountId }.OfType<Guid>())
        {
            _ = await accounts.FindAsync(accountId, cancellationToken)
                ?? throw new DomainException("Счёт операции больше не существует.");
        }

        transactions.Add(deleted);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        notifier.NotifyChanged();
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

    private async Task<(Account From, Account To)> LoadAccountsAsync(TransferInput input, CancellationToken cancellationToken)
    {
        var from = await accounts.FindAsync(input.FromAccountId, cancellationToken)
            ?? throw new DomainException("Счёт списания не найден.");
        var to = await accounts.FindAsync(input.ToAccountId, cancellationToken)
            ?? throw new DomainException("Счёт зачисления не найден.");
        return (from, to);
    }

    /// <summary>
    /// A receipt in another currency becomes an amount in the account's: at the rate the bank charged if known,
    /// otherwise at the CBR rate of the date. «20 EUR» goes into the note, so the receipt can still be matched.
    /// </summary>
    private async Task<(TransactionInput Input, bool IsFallback)> ConvertPaidAsync(TransactionInput input, Account account, CancellationToken cancellationToken)
    {
        if (input.PaidCurrency is not { } paid || paid == account.Currency)
        {
            return (input with { PaidCurrency = null, PaidRate = null }, false);
        }

        var (rate, isFallback) = input.PaidRate is { } manual
            ? (manual, false)
            : await QuoteAsync(paid, account.Currency, input.Date, cancellationToken);
        if (rate <= 0)
        {
            throw new DomainException("Курс — число больше нуля.");
        }

        var converted = decimal.Round(input.Amount * rate, 2, MidpointRounding.AwayFromZero);
        var receipt = $"{input.Amount.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))} {paid}";
        var note = string.IsNullOrWhiteSpace(input.Note) ? receipt : $"{input.Note.Trim()} · {receipt}";
        return (input with { Amount = converted, Note = note, PaidCurrency = null, PaidRate = null }, isFallback);
    }

    private async Task<(decimal Rate, bool IsFallback)> QuoteAsync(CurrencyCode from, CurrencyCode to, DateOnly date, CancellationToken cancellationToken)
    {
        var quote = await exchangeRates.GetRateAsync(from, to, date, cancellationToken);
        return (quote.Rate, quote.IsFallback);
    }

    private Task<(decimal Rate, bool IsFallback)> ResolveRateAsync(TransactionInput input, Account account, CancellationToken cancellationToken) =>
        ResolveRateAsync(input.ExchangeRate, account, input.Date, cancellationToken);

    private async Task<(decimal Rate, bool IsFallback)> ResolveRateAsync(decimal? manual, Account account, DateOnly date, CancellationToken cancellationToken)
    {
        if (manual is { } rate)
        {
            return (rate, false);
        }

        var baseCurrency = settings.Load().BaseCurrency;
        if (account.Currency == baseCurrency)
        {
            return (1m, false);
        }

        var quote = await exchangeRates.GetRateAsync(account.Currency, baseCurrency, date, cancellationToken);
        return (quote.Rate, quote.IsFallback);
    }

    /// <summary>Same currency — the same amount; otherwise what the user entered or the CBR cross rate.</summary>
    private async Task<(decimal? ToAmount, bool IsFallback)> ResolveToAmountAsync(
        TransferInput input, Account from, Account to, CancellationToken cancellationToken)
    {
        if (from.Currency == to.Currency || input.ToAmount is not null)
        {
            return (input.ToAmount, false);
        }

        var quote = await exchangeRates.GetRateAsync(from.Currency, to.Currency, input.Date, cancellationToken);
        return (decimal.Round(input.Amount * quote.Rate, 2, MidpointRounding.AwayFromZero), quote.IsFallback);
    }

    private static DomainException NotFound(Guid id) => new($"Операция {id} не найдена.");
}
