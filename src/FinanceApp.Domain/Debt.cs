namespace FinanceApp.Domain;

/// <summary>Who owes whom.</summary>
public enum DebtDirection
{
    /// <summary>The user lent money: «мне должны».</summary>
    Lent,

    /// <summary>The user borrowed money: «я должен».</summary>
    Borrowed,
}

/// <summary>
/// Money lent to someone or borrowed from someone: «Дал Саше 5 000 ₽ — вернёт к 15-му». A note kept apart from the
/// accounts: it changes no balance, so the totals stay about income and spending. Repayments are added up in
/// <see cref="Repaid"/>; once all is back the debt is closed and kept for history.
/// </summary>
public sealed class Debt
{
    public const int MaxPersonLength = 60;

    private Debt(Guid id, DateTime createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public DebtDirection Direction { get; private set; }

    /// <summary>Who: «Саша», «Банк».</summary>
    public string Person { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public CurrencyCode Currency { get; private set; }

    /// <summary>How much has come back (or been paid back) so far, in <see cref="Currency"/>.</summary>
    public decimal Repaid { get; private set; }

    /// <summary>The day the money was given or taken.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>When it should be back; <c>null</c> — no date agreed.</summary>
    public DateOnly? DueDate { get; private set; }

    public string? Note { get; private set; }

    /// <summary>The day the last of it came back; <c>null</c> while something is still owed.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; private set; }

    public decimal Left => Amount - Repaid;

    public bool IsClosed => ClosedOn is not null;

    /// <summary>The due date has passed and something is still owed.</summary>
    public bool IsOverdue(DateOnly today) => !IsClosed && DueDate is { } due && due < today;

    public static Debt Create(
        DebtDirection direction,
        string person,
        decimal amount,
        CurrencyCode currency,
        DateOnly date,
        DateOnly? dueDate,
        string? note,
        DateTime nowUtc,
        Guid? id = null)
    {
        var debt = new Debt(id ?? Guid.CreateVersion7(), nowUtc);
        debt.Update(direction, person, amount, currency, date, dueDate, note);
        return debt;
    }

    public void Update(
        DebtDirection direction,
        string person,
        decimal amount,
        CurrencyCode currency,
        DateOnly date,
        DateOnly? dueDate,
        string? note)
    {
        var name = person?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new DomainException("Укажите, кто должен или кому.");
        }

        if (name.Length > MaxPersonLength)
        {
            throw new DomainException($"Имя длиннее {MaxPersonLength} символов.");
        }

        CheckAmount(amount);
        if (amount < Repaid)
        {
            throw new DomainException("Сумма долга меньше того, что уже вернули.");
        }

        if (dueDate is { } due && due < date)
        {
            throw new DomainException("Срок возврата раньше дня, когда дали деньги.");
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > Transaction.MaxNoteLength)
        {
            throw new DomainException($"Заметка длиннее {Transaction.MaxNoteLength} символов.");
        }

        Direction = direction;
        Person = name;
        Amount = amount;
        Currency = currency;
        Date = date;
        DueDate = dueDate;
        Note = trimmed;
        if (Repaid < Amount)
        {
            ClosedOn = null;
        }
    }

    /// <summary>Part (or all) of it came back on <paramref name="date"/>; the whole sum closes the debt.</summary>
    public void Repay(decimal amount, DateOnly date)
    {
        CheckAmount(amount);
        if (IsClosed)
        {
            throw new DomainException("Этот долг уже закрыт.");
        }

        if (amount > Left)
        {
            throw new DomainException("Возврат больше остатка долга.");
        }

        Repaid += amount;
        if (Repaid == Amount)
        {
            ClosedOn = date < Date ? Date : date;
        }
    }

    private static void CheckAmount(decimal amount)
    {
        if (amount <= 0 || amount > Transaction.MaxAmount || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Сумма — больше нуля, не больше двух знаков после запятой.");
        }
    }
}
