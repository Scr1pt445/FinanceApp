using FinanceApp.Domain;

namespace FinanceApp.Application.Csv;

/// <summary>
/// One line of the transactions CSV (the original contract plus extension columns, ADR 0008 and 0011).
/// </summary>
/// <param name="Amount">Amount in the base currency — what consumers sum up.</param>
/// <param name="Account">The account paid from or into; the source of a transfer.</param>
/// <param name="Currency">Account currency; <c>null</c> when the file has no <c>currency</c> column (base currency).</param>
/// <param name="OriginalAmount">Amount in <paramref name="Currency"/>; <c>null</c> when absent.</param>
/// <param name="Id">Transaction id; <c>null</c> for files from other sources (e.g. the 2025 archive).</param>
/// <param name="LineNumber">Source line for error messages; 0 when the row was built for export.</param>
/// <param name="ToAccount">Destination of a transfer.</param>
/// <param name="ToAmount">Amount received by <paramref name="ToAccount"/>, in its currency.</param>
public sealed record TransactionCsvRow(
    DateOnly Date,
    TransactionType Type,
    decimal Amount,
    string? Category,
    string? Account,
    string? Note,
    CurrencyCode? Currency = null,
    decimal? OriginalAmount = null,
    Guid? Id = null,
    int LineNumber = 0,
    string? ToAccount = null,
    decimal? ToAmount = null);
