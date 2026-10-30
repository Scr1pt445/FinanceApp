using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Application.Recurring;

/// <summary>What a recurring operation is made of; the amount is in the account currency.</summary>
/// <param name="StartAfter">
/// <see cref="NextDate"/> is the day of an operation just recorded (the form's «Повторять»): the schedule keeps to
/// that day and the first one due is the next time.
/// </param>
public sealed record RecurringInput(
    TransactionType Type,
    decimal Amount,
    Guid? CategoryId,
    Guid AccountId,
    Guid? ToAccountId,
    string? Note,
    RecurrenceFrequency Frequency,
    DateOnly NextDate,
    bool StartAfter = false);

/// <summary>
/// Rent, subscriptions, the salary: kept with their schedule and offered on the day they are due. Recording one
/// makes an ordinary operation dated that day; skipping moves on to the next time. Nothing is recorded on its own —
/// the amount may have changed, or the payment may not have happened.
/// </summary>
public sealed class RecurringService(
    IRecurringOperationRepository recurring,
    ICategoryRepository categories,
    IAccountRepository accounts,
    TransactionService transactions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    DataChangedNotifier notifier)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public Task<IReadOnlyList<RecurringOperation>> ListAsync(CancellationToken cancellationToken = default) =>
        recurring.ListAsync(cancellationToken);

    /// <summary>Due today or earlier, the oldest first.</summary>
    public async Task<IReadOnlyList<RecurringOperation>> DueAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        return (await recurring.ListAsync(cancellationToken)).Where(r => r.IsDue(today)).ToList();
    }

    public async Task<RecurringOperation> AddAsync(RecurringInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CheckReferencesAsync(input, cancellationToken);
        var operation = RecurringOperation.Create(
            input.Type, input.Amount, input.CategoryId, input.AccountId, input.ToAccountId, input.Note, input.Frequency, input.NextDate, Now);
        if (input.StartAfter)
        {
            operation.Advance();
        }

        recurring.Add(operation);
        await SaveAsync(cancellationToken);
        return operation;
    }

    public async Task<RecurringOperation> UpdateAsync(Guid id, RecurringInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var operation = await recurring.FindAsync(id, cancellationToken) ?? throw NotFound();
        await CheckReferencesAsync(input, cancellationToken);
        operation.Update(input.Type, input.Amount, input.CategoryId, input.AccountId, input.ToAccountId, input.Note, input.Frequency, input.NextDate);
        await SaveAsync(cancellationToken);
        return operation;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var operation = await recurring.FindAsync(id, cancellationToken) ?? throw NotFound();
        recurring.Remove(operation);
        await SaveAsync(cancellationToken);
    }

    /// <summary>Records the operation for the day it was due, with <paramref name="amount"/> if it differed this time.</summary>
    public async Task<SavedTransaction> RecordAsync(Guid id, decimal? amount = null, CancellationToken cancellationToken = default)
    {
        var operation = await recurring.FindAsync(id, cancellationToken) ?? throw NotFound();
        var saved = await unitOfWork.InTransactionAsync(
            async ct =>
            {
                var date = operation.NextDate;
                var sum = amount ?? operation.Amount;
                var result = operation.Type == TransactionType.Transfer
                    ? await transactions.AddTransferAsync(new TransferInput(date, sum, operation.AccountId, operation.ToAccountId!.Value, operation.Note), ct)
                    : await transactions.AddAsync(new TransactionInput(date, operation.Type, sum, operation.CategoryId!.Value, operation.AccountId, operation.Note), ct);
                operation.Advance();
                await unitOfWork.SaveChangesAsync(ct);
                return result;
            },
            cancellationToken);
        notifier.NotifyChanged();
        return saved;
    }

    /// <summary>Not this time: on to the next date.</summary>
    public async Task SkipAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var operation = await recurring.FindAsync(id, cancellationToken) ?? throw NotFound();
        operation.Advance();
        await SaveAsync(cancellationToken);
    }

    private async Task CheckReferencesAsync(RecurringInput input, CancellationToken cancellationToken)
    {
        var all = await accounts.ListAsync(cancellationToken);
        if (all.All(a => a.Id != input.AccountId) || (input.ToAccountId is { } to && all.All(a => a.Id != to)))
        {
            throw new DomainException("Счёт не найден.");
        }

        if (input.CategoryId is { } categoryId
            && (await categories.FindAsync(categoryId, cancellationToken) is not { } category || category.Type != input.Type))
        {
            throw new DomainException("Категория не найдена или не подходит к типу операции.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        notifier.NotifyChanged();
    }

    private static DomainException NotFound() => new("Регулярная операция не найдена.");
}
