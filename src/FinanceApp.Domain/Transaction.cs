namespace FinanceApp.Domain;

/// <summary>
/// One money movement. <see cref="Amount"/> is in the account currency; <see cref="ExchangeRate"/>
/// converts it to the base currency and is fixed on the transaction date, so past totals never drift.
/// </summary>
public sealed class Transaction
{
    public const int MaxNoteLength = 500;

    /// <summary>Upper bound that keeps amounts sane and far from decimal overflow.</summary>
    public const decimal MaxAmount = 1_000_000_000_000m;

    private Transaction(Guid id, DateTime createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public DateOnly Date { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>Positive amount in <see cref="Currency"/>, at most two decimal places.</summary>
    public decimal Amount { get; private set; }

    public CurrencyCode Currency { get; private set; }

    /// <summary>Units of the base currency per one unit of <see cref="Currency"/>.</summary>
    public decimal ExchangeRate { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid AccountId { get; private set; }

    public string? Note { get; private set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>UTC.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Amount in the base currency, rounded to kopecks (midpoint away from zero).</summary>
    public decimal BaseAmount => Math.Round(Amount * ExchangeRate, 2, MidpointRounding.AwayFromZero);

    public static Transaction Create(
        DateOnly date,
        TransactionType type,
        decimal amount,
        decimal exchangeRate,
        Category category,
        Account account,
        string? note,
        DateTime nowUtc,
        Guid? id = null)
    {
        var transaction = new Transaction(id ?? Guid.CreateVersion7(), EnsureUtc(nowUtc));
        if (transaction.Id == Guid.Empty)
        {
            throw new DomainException("Идентификатор операции не задан.");
        }

        transaction.Apply(date, type, amount, exchangeRate, category, account, note);
        return transaction;
    }

    public void Update(
        DateOnly date,
        TransactionType type,
        decimal amount,
        decimal exchangeRate,
        Category category,
        Account account,
        string? note,
        DateTime nowUtc)
    {
        Apply(date, type, amount, exchangeRate, category, account, note);
        UpdatedAt = EnsureUtc(nowUtc);
    }

    private void Apply(
        DateOnly date,
        TransactionType type,
        decimal amount,
        decimal exchangeRate,
        Category category,
        Account account,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(account);

        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Неизвестный тип операции: {type}.");
        }

        if (amount <= 0)
        {
            throw new DomainException("Сумма должна быть больше нуля.");
        }

        if (amount > MaxAmount)
        {
            throw new DomainException("Сумма слишком большая.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("В сумме не может быть больше двух знаков после запятой.");
        }

        if (exchangeRate <= 0)
        {
            throw new DomainException("Курс валюты должен быть больше нуля.");
        }

        if (category.Type != type)
        {
            throw new DomainException($"Категория «{category.Name}» не подходит для этого типа операции.");
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > MaxNoteLength)
        {
            throw new DomainException($"Комментарий длиннее {MaxNoteLength} символов.");
        }

        Date = date;
        Type = type;
        Amount = amount;
        Currency = account.Currency;
        ExchangeRate = exchangeRate;
        CategoryId = category.Id;
        AccountId = account.Id;
        Note = trimmedNote;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
