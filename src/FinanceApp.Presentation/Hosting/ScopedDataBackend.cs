using FinanceApp.Application.Backups;
using FinanceApp.Application.Csv;
using FinanceApp.Application.Export;
using FinanceApp.Presentation.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Presentation.Hosting;

/// <summary>The «Данные» screen's backend: the export runs in its own DI scope, copies use the shared service.</summary>
public sealed class ScopedDataBackend(IServiceScopeFactory scopes, IDatabaseBackup backups) : IDataBackend
{
    public const string TransactionsFileName = "transactions.csv";

    public async Task<ExportedCsv> ExportCsvAsync(string folder, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, TransactionsFileName);
        await using var scope = scopes.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<CsvExportService>().ExportAsync(path, cancellationToken);
        return new ExportedCsv(rows, [path, Path.Combine(folder, AccountsCsv.FileName)]);
    }

    public Task<BackupInfo> BackupAsync(CancellationToken cancellationToken = default) => backups.CreateAsync(cancellationToken);

    public IReadOnlyList<BackupInfo> ListBackups() => backups.List();
}
