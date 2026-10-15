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

        var history = await transactions.ListAsync(cancellationToken: cancellationToken);
        var allAccounts = await accounts.ListAsync(cancellationToken);
        var rows = BuildRows(history, await categories.ListAsync(cancellationToken), allAccounts);

        // accounts.csv lives next to transactions.csv (ADR 0011); each file is replaced atomically.
        var accountsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, AccountsCsv.FileName);
        var accountRows = allAccounts.Select(a => AccountsCsv.Row(a, a.OpeningBalance + history.Sum(t => t.EffectOn(a.Id))));

        await fileWriter.WriteAtomicallyAsync(path, TransactionCsvWriter.WriteToBytes(rows), cancellationToken);
        await fileWriter.WriteAtomicallyAsync(accountsPath, AccountsCsv.WriteToBytes(accountRows), cancellationToken);
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
                t.CategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null,
                accountNames.GetValueOrDefault(t.AccountId),
                t.Note,
                t.Currency,
                t.Amount,
                t.Id,
                ToAccount: t.ToAccountId is { } toId ? accountNames.GetValueOrDefault(toId) : null,
                ToAmount: t.ToAmount))
            .ToList();
    }
}
