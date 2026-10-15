namespace FinanceApp.Domain;

public enum AccountKind
{
    /// <summary>A place money is paid from: a card, cash.</summary>
    Regular = 1,

    /// <summary>Money put aside for a purpose («Машина», «Отдых»), like a piggy bank in a banking app (ADR 0010).</summary>
    Envelope = 2,
}

/// <summary>
/// An account or an envelope. Fixes the currency of its transactions. The balance is computed from
/// transactions on top of an optional opening balance — accounts are a convenience, not a mirror of a bank.
/// </summary>
public sealed class Account
{
    public const int MaxNameLength = 100;

    public Account(
        Guid id,
        string name,
        CurrencyCode currency,
        int sortOrder = 0,
        AccountKind kind = AccountKind.Regular,
        decimal openingBalance = 0)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Идентификатор счёта не задан.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Неизвестный вид счёта: {kind}.");
        }

        Id = id;
        Name = NormalizeName(name);
        Currency = currency;
        SortOrder = sortOrder;
        Kind = kind;
        OpeningBalance = ValidateMoney(openingBalance, allowNegative: true, "Начальный остаток");
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public CurrencyCode Currency { get; private set; }

    public AccountKind Kind { get; private set; }

    public bool IsEnvelope => Kind == AccountKind.Envelope;

    /// <summary>What was on the account before the first recorded transaction; may be negative (a debt).</summary>
    public decimal OpeningBalance { get; private set; }

    /// <summary>Envelope goal: how much to collect. <c>null</c> — no goal, nothing goal-related is shown.</summary>
    public decimal? GoalAmount { get; private set; }

    /// <summary>Optional date by which the goal should be reached.</summary>
    public DateOnly? GoalDate { get; private set; }

    public bool IsHidden { get; private set; }

    public int SortOrder { get; private set; }

    public void Rename(string name) => Name = NormalizeName(name);

    public void Hide() => IsHidden = true;

    public void Show() => IsHidden = false;

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    public void SetOpeningBalance(decimal amount) =>
        OpeningBalance = ValidateMoney(amount, allowNegative: true, "Начальный остаток");

    public void SetGoal(decimal amount, DateOnly? date)
    {
        if (!IsEnvelope)
        {
            throw new DomainException("Цель можно задать только конверту.");
        }

        if (amount <= 0)
        {
            throw new DomainException("Цель должна быть больше нуля.");
        }

        GoalAmount = ValidateMoney(amount, allowNegative: false, "Цель");
        GoalDate = date;
    }

    public void ClearGoal()
    {
        GoalAmount = null;
        GoalDate = null;
    }

    public override string ToString() => $"{Name} ({Currency})";

    private static decimal ValidateMoney(decimal amount, bool allowNegative, string what)
    {
        if (!allowNegative && amount < 0)
        {
            throw new DomainException($"{what} не может быть отрицательным.");
        }

        if (Math.Abs(amount) > Transaction.MaxAmount)
        {
            throw new DomainException($"{what} слишком большой.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException($"{what}: не больше двух знаков после запятой.");
        }

        return amount;
    }

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
