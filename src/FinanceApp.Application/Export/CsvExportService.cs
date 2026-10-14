using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Csv;
using FinanceApp.Domain;

namespace FinanceApp.Application.Export;

/// <summary>Builds the full CSV snapshot of all transactions and writes it atomically.</summary>
public sealed class CsvExportService(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IAccountRepository accounts,
    IFileWriter fileWriter)
{
    public async Task<int> ExportAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var rows = BuildRows(
            await transactions.ListAsync(cancellationToken: cancellationToken),
            await categories.ListAsync(cancellationToken),
            await accounts.ListAsync(cancellationToken));

        await fileWriter.WriteAtomicallyAsync(path, TransactionCsvWriter.WriteToBytes(rows), cancellationToken);
        return rows.Count;
    }

    /// <summary>Rows ordered by date, then by creation time — stable across exports.</summary>
    public static IReadOnlyList<TransactionCsvRow> BuildRows(
        IEnumerable<Transaction> transactions,
        IEnumerable<Category> categories,
        IEnumerable<Account> accounts)
    {
        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);
        var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);

        return transactions
            .OrderBy(t => t.Date)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Select(t => new TransactionCsvRow(
                t.Date,
                t.Type,
                t.BaseAmount,
                categoryNames.GetValueOrDefault(t.CategoryId),
                accountNames.GetValueOrDefault(t.AccountId),
                t.Note,
                t.Currency,
                t.Amount,
                t.Id))
            .ToList();
    }
}
