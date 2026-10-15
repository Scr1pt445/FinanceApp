namespace FinanceApp.Domain;

/// <summary>
/// One money movement. <see cref="Amount"/> is in the account currency; <see cref="ExchangeRate"/>
/// converts it to the base currency and is fixed on the transaction date, so past totals never drift.
/// A transfer moves <see cref="Amount"/> out of <see cref="AccountId"/> and <see cref="ToAmount"/> into
/// <see cref="ToAccountId"/> (ADR 0010).
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

    public bool IsTransfer => Type == TransactionType.Transfer;

    /// <summary>Positive amount in <see cref="Currency"/>, at most two decimal places.</summary>
    public decimal Amount { get; private set; }

    public CurrencyCode Currency { get; private set; }

    /// <summary>Units of the base currency per one unit of <see cref="Currency"/>.</summary>
    public decimal ExchangeRate { get; private set; }

    /// <summary>Required for income and expense, <c>null</c> for a transfer.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>The account paid from or into; the source of a transfer.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>Destination of a transfer; <c>null</c> otherwise.</summary>
    public Guid? ToAccountId { get; private set; }

    /// <summary>Amount received by <see cref="ToAccountId"/>, in its currency; <c>null</c> unless a transfer.</summary>
    public decimal? ToAmount { get; private set; }

    public string? Note { get; private set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>UTC.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Amount in the base currency, rounded to kopecks (midpoint away from zero).</summary>
    public decimal BaseAmount => Math.Round(Amount * ExchangeRate, 2, MidpointRounding.AwayFromZero);

    /// <summary>Income or expense.</summary>
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
        var transaction = New(id, nowUtc);
        transaction.Apply(date, type, amount, exchangeRate, category, account, note);
        return transaction;
    }

    /// <param name="toAmount">Received amount; may be omitted when both accounts share a currency.</param>
    public static Transaction CreateTransfer(
        DateOnly date,
        decimal amount,
        decimal exchangeRate,
        Account from,
        Account to,
        decimal? toAmount,
        string? note,
        DateTime nowUtc,
        Guid? id = null)
    {
        var transaction = New(id, nowUtc);
        transaction.ApplyTransfer(date, amount, exchangeRate, from, to, toAmount, note);
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

    public void UpdateTransfer(
        DateOnly date,
        decimal amount,
        decimal exchangeRate,
        Account from,
        Account to,
        decimal? toAmount,
        string? note,
        DateTime nowUtc)
    {
        ApplyTransfer(date, amount, exchangeRate, from, to, toAmount, note);
        UpdatedAt = EnsureUtc(nowUtc);
    }

    /// <summary>How this transaction changes the balance of <paramref name="accountId"/>, in that account's currency.</summary>
    public decimal EffectOn(Guid accountId) =>
        Type switch
        {
            TransactionType.Income when AccountId == accountId => Amount,
            TransactionType.Expense when AccountId == accountId => -Amount,
            TransactionType.Transfer when AccountId == accountId && ToAccountId == accountId => 0,
            TransactionType.Transfer when AccountId == accountId => -Amount,
            TransactionType.Transfer when ToAccountId == accountId => ToAmount!.Value,
            _ => 0,
        };

    private static Transaction New(Guid? id, DateTime nowUtc)
    {
        var transaction = new Transaction(id ?? Guid.CreateVersion7(), EnsureUtc(nowUtc));
        return transaction.Id == Guid.Empty
            ? throw new DomainException("Идентификатор операции не задан.")
            : transaction;
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

        if (type == TransactionType.Transfer)
        {
            throw new DomainException("Перевод создаётся с двумя счетами, без категории.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Неизвестный тип операции: {type}.");
        }

        ValidateAmount(amount);
        ValidateRate(exchangeRate);

        if (category.Type != type)
        {
            throw new DomainException($"Категория «{category.Name}» не подходит для этого типа операции.");
        }

        var trimmedNote = NormalizeNote(note);

        Date = date;
        Type = type;
        Amount = amount;
        Currency = account.Currency;
        ExchangeRate = exchangeRate;
        CategoryId = category.Id;
        AccountId = account.Id;
        ToAccountId = null;
        ToAmount = null;
        Note = trimmedNote;
    }

    private void ApplyTransfer(
        DateOnly date,
        decimal amount,
        decimal exchangeRate,
        Account from,
        Account to,
        decimal? toAmount,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (from.Id == to.Id)
        {
            throw new DomainException("Перевод должен быть между разными счетами.");
        }

        ValidateAmount(amount);
        ValidateRate(exchangeRate);

        decimal received;
        if (from.Currency == to.Currency)
        {
            if (toAmount is { } given && given != amount)
            {
                throw new DomainException("Счета в одной валюте: зачисляется та же сумма, что списана.");
            }

            received = amount;
        }
        else
        {
            received = toAmount ?? throw new DomainException($"Укажите, сколько {to.Currency} зачислено на «{to.Name}».");
            ValidateAmount(received);
        }

        var trimmedNote = NormalizeNote(note);

        Date = date;
        Type = TransactionType.Transfer;
        Amount = amount;
        Currency = from.Currency;
        ExchangeRate = exchangeRate;
        CategoryId = null;
        AccountId = from.Id;
        ToAccountId = to.Id;
        ToAmount = received;
        Note = trimmedNote;
    }

    private static void ValidateAmount(decimal amount)
    {
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
    }

    private static void ValidateRate(decimal exchangeRate)
    {
        if (exchangeRate <= 0)
        {
            throw new DomainException("Курс валюты должен быть больше нуля.");
        }
    }

    private static string? NormalizeNote(string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return trimmed?.Length > MaxNoteLength
            ? throw new DomainException($"Комментарий длиннее {MaxNoteLength} символов.")
            : trimmed;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
