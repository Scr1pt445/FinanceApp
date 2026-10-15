namespace FinanceApp.Domain;

/// <summary>Kind of money movement. The amount is always positive; the type gives it meaning.</summary>
public enum TransactionType
{
    Income = 1,
    Expense = 2,

    /// <summary>
    /// Legacy, CSV import only: money put aside before envelopes existed (export format v1). Not a domain
    /// transaction any more — imported and migrated rows become transfers into envelopes (ADR 0010).
    /// </summary>
    Saving = 3,

    /// <summary>Money moved between two accounts or envelopes; no category, does not change income or expenses.</summary>
    Transfer = 4,
}
