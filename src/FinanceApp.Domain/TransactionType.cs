namespace FinanceApp.Domain;

/// <summary>Kind of money movement. The amount is always positive; the type gives it meaning.</summary>
public enum TransactionType
{
    Income = 1,
    Expense = 2,

    /// <summary>
    /// Legacy: money put aside without an envelope. Being replaced by transfers into envelopes (ADR 0010);
    /// existing rows are converted by a migration.
    /// </summary>
    Saving = 3,

    /// <summary>Money moved between two accounts or envelopes; no category, does not change income or expenses.</summary>
    Transfer = 4,
}
