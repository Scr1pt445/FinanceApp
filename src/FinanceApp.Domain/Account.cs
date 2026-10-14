namespace FinanceApp.Domain;

/// <summary>Where the money is: a card, cash, a deposit. The account fixes the currency of its transactions.</summary>
public sealed class Account
{
    public const int MaxNameLength = 100;

    public Account(Guid id, string name, CurrencyCode currency, int sortOrder = 0)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Идентификатор счёта не задан.");
        }

        Id = id;
        Name = NormalizeName(name);
        Currency = currency;
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public CurrencyCode Currency { get; private set; }

    public bool IsHidden { get; private set; }

    public int SortOrder { get; private set; }

    public void Rename(string name) => Name = NormalizeName(name);

    public void Hide() => IsHidden = true;

    public void Show() => IsHidden = false;

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    public override string ToString() => $"{Name} ({Currency})";

    private static string NormalizeName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException("Название счёта не может быть пустым.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainException($"Название счёта длиннее {MaxNameLength} символов.");
        }

        return trimmed;
    }
}
