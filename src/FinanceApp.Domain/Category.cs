namespace FinanceApp.Domain;

/// <summary>
/// A category belongs to exactly one transaction type, so «Прочее» can exist
/// both as an income and as an expense category. (Name, Type) is unique.
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

        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Неизвестный тип операции: {type}.");
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
