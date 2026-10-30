namespace FinanceApp.Domain;

/// <summary>
/// A category belongs to income or expense, so «Прочее» can exist as both. (Name, Type) is unique.
/// Transfers have no category (ADR 0010).
/// </summary>
public sealed class Category
{
    public const int MaxNameLength = 100;

    public Category(Guid id, string name, TransactionType type, int sortOrder = 0)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Идентификатор категории не задан.");
        }

        if (type is not (TransactionType.Income or TransactionType.Expense))
        {
            throw new DomainException("Категории бывают только у доходов и расходов.");
        }

        Id = id;
        Name = NormalizeName(name);
        Type = type;
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>Hidden categories are kept for history but not offered for new transactions.</summary>
    public bool IsHidden { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>How much may be spent in a month, in the base currency; <c>null</c> — no limit. Expenses only.</summary>
    public decimal? MonthlyLimit { get; private set; }

    /// <param name="limit"><c>null</c> removes the limit.</param>
    public void SetLimit(decimal? limit)
    {
        if (limit is { } value)
        {
            if (Type != TransactionType.Expense)
            {
                throw new DomainException("Лимит ставится только на категории расходов.");
            }

            if (value <= 0 || value > Transaction.MaxAmount || decimal.Round(value, 2) != value)
            {
                throw new DomainException("Лимит — сумма больше нуля, не больше двух знаков после запятой.");
            }
        }

        MonthlyLimit = limit;
    }

    public void Rename(string name) => Name = NormalizeName(name);

    public void Hide() => IsHidden = true;

    public void Show() => IsHidden = false;

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    public override string ToString() => $"{Name} ({Type})";

    private static string NormalizeName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException("Название категории не может быть пустым.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainException($"Название категории длиннее {MaxNameLength} символов.");
        }

        return trimmed;
    }
}
