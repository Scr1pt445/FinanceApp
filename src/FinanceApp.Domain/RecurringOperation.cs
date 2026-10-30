namespace FinanceApp.Domain;

/// <summary>How often a recurring operation comes round.</summary>
public enum RecurrenceFrequency
{
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>
/// A payment that comes round on its own — rent, a subscription, the salary. It is not money yet: on
/// <see cref="NextDate"/> the app offers to record it as an operation (or to skip this time), and moves on.
/// Monthly ones keep their day: the 31st falls on the last day of shorter months and comes back afterwards.
/// </summary>
public sealed class RecurringOperation
{
    private RecurringOperation(Guid id, DateTime createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>In the currency of <see cref="AccountId"/>.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Required for income and expense, <c>null</c> for a transfer.</summary>
    public Guid? CategoryId { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>Destination of a transfer; <c>null</c> otherwise.</summary>
    public Guid? ToAccountId { get; private set; }

    public string? Note { get; private set; }

    public RecurrenceFrequency Frequency { get; private set; }

    /// <summary>The day the next operation is due.</summary>
    public DateOnly NextDate { get; private set; }

    /// <summary>The day of the month (or of the week, as 1 = Monday … 7 = Sunday) the operation keeps to.</summary>
    public int Anchor { get; private set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <param name="next">The first day it is due.</param>
    public static RecurringOperation Create(
        TransactionType type,
        decimal amount,
        Guid? categoryId,
        Guid accountId,
        Guid? toAccountId,
        string? note,
        RecurrenceFrequency frequency,
        DateOnly next,
        DateTime nowUtc,
        Guid? id = null)
    {
        var operation = new RecurringOperation(id ?? Guid.CreateVersion7(), nowUtc);
        operation.Update(type, amount, categoryId, accountId, toAccountId, note, frequency, next);
        return operation;
    }

    public void Update(
        TransactionType type,
        decimal amount,
        Guid? categoryId,
        Guid accountId,
        Guid? toAccountId,
        string? note,
        RecurrenceFrequency frequency,
        DateOnly next)
    {
        if (amount <= 0 || amount > Transaction.MaxAmount || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Сумма — больше нуля, не больше двух знаков после запятой.");
        }

        if (type == TransactionType.Transfer)
        {
            if (toAccountId is not { } to || to == accountId)
            {
                throw new DomainException("У перевода должны быть разные счета «откуда» и «куда».");
            }

            if (categoryId is not null)
            {
                throw new DomainException("У перевода нет категории.");
            }
        }
        else
        {
            if (categoryId is null)
            {
                throw new DomainException("Выберите категорию.");
            }

            toAccountId = null;
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > Transaction.MaxNoteLength)
        {
            throw new DomainException($"Заметка длиннее {Transaction.MaxNoteLength} символов.");
        }

        Type = type;
        Amount = amount;
        CategoryId = categoryId;
        AccountId = accountId;
        ToAccountId = toAccountId;
        Note = trimmed;
        Frequency = frequency;
        NextDate = next;
        Anchor = frequency == RecurrenceFrequency.Weekly ? ((int)next.DayOfWeek + 6) % 7 + 1 : next.Day;
    }

    /// <summary>Whether it should be offered on <paramref name="today"/> (also when days were missed).</summary>
    public bool IsDue(DateOnly today) => NextDate <= today;

    /// <summary>Done or skipped for <see cref="NextDate"/>: on to the next time.</summary>
    public void Advance() => NextDate = After(NextDate, Frequency, Anchor);

    /// <summary>The day after <paramref name="date"/> by the schedule, keeping the anchor day where the month has it.</summary>
    public static DateOnly After(DateOnly date, RecurrenceFrequency frequency, int anchor)
    {
        switch (frequency)
        {
            case RecurrenceFrequency.Weekly:
                return date.AddDays(7);
            case RecurrenceFrequency.Yearly:
            {
                var year = date.Year + 1;
                return new DateOnly(year, date.Month, Math.Min(anchor, DateTime.DaysInMonth(year, date.Month)));
            }

            default:
            {
                var month = new DateOnly(date.Year, date.Month, 1).AddMonths(1);
                return new DateOnly(month.Year, month.Month, Math.Min(anchor, DateTime.DaysInMonth(month.Year, month.Month)));
            }
        }
    }
}
