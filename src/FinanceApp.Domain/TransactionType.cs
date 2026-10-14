namespace FinanceApp.Domain;

/// <summary>Kind of money movement. The amount is always positive; the type gives it meaning.</summary>
public enum TransactionType
{
    Income = 1,
    Expense = 2,

    /// <summary>Money put aside (savings, investments). Reduces the month's balance like an expense.</summary>
    Saving = 3,
}
