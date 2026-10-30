using System.Globalization;
using FinanceApp.Application.Backups;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Infrastructure.Backups;

/// <summary>
/// Puts a copy in place of <c>finance.db</c>: checked first, the current data copied to the backups, the pools of
/// open connections dropped, the file swapped in one move, then migrated like any older database.
/// </summary>
public sealed class SqliteDatabaseRestore(FinancePaths paths, IDatabaseBackup backups, ISettingsStore settings, IServiceScopeFactory scopes)
    : IDatabaseRestore
{
    private static readonly string[] RequiredTables = ["Transactions", "Accounts", "Categories"];

    public async Task<BackupContents> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException("Файл не найден.");
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken);

            if (!string.Equals(await ScalarAsync(connection, "PRAGMA quick_check", cancellationToken) as string, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Копия повреждена.");
            }

            foreach (var table in RequiredTables)
            {
                var found = await ScalarAsync(connection, $"SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'", cancellationToken);
                if (Convert.ToInt64(found, CultureInfo.InvariantCulture) == 0)
                {
                    throw new InvalidDataException("Это не копия базы FinanceApp.");
                }
            }

            var transactions = Convert.ToInt32(await ScalarAsync(connection, "SELECT count(*) FROM Transactions", cancellationToken), CultureInfo.InvariantCulture);
            var accounts = Convert.ToInt32(await ScalarAsync(connection, "SELECT count(*) FROM Accounts", cancellationToken), CultureInfo.InvariantCulture);
            var last = await ScalarAsync(connection, "SELECT max(Date) FROM Transactions", cancellationToken) as string;
            DateOnly? lastDate = DateOnly.TryParse(last, CultureInfo.InvariantCulture, out var d) ? d : null;
            return new BackupContents(transactions, accounts, lastDate);
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("Это не база данных FinanceApp или файл повреждён.", ex);
        }
    }

    public async Task<RestoreResult> RestoreAsync(string path, CancellationToken cancellationToken = default)
    {
        var contents = await InspectAsync(path, cancellationToken);

        // Taken first: the copy may itself sit in the backups folder, where the safety copy is about to be written.
        var incoming = paths.DatabasePath + ".restoring";
        File.Copy(path, incoming, overwrite: true);

        // The data being replaced is kept: a wrong file must not cost anything.
        BackupInfo? safety = File.Exists(paths.DatabasePath) ? await backups.CreateAsync(cancellationToken) : null;

        SqliteConnection.ClearAllPools();
        foreach (var journal in new[] { "-wal", "-shm", "-journal" })
        {
            File.Delete(paths.DatabasePath + journal);
        }

        File.Move(incoming, paths.DatabasePath, overwrite: true);

        var baseCurrency = settings.Load().BaseCurrency;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
            baseCurrency = await GuessBaseCurrencyAsync(db, cancellationToken) ?? baseCurrency;
        }

        if (baseCurrency != settings.Load().BaseCurrency)
        {
            await settings.SaveAsync(settings.Load() with { BaseCurrency = baseCurrency, TotalsCurrency = null }, cancellationToken);
        }

        return new RestoreResult(contents, baseCurrency, safety);
    }

    /// <summary>
    /// The base currency is a setting, not stored in the database; the copy's operations tell it: those of an
    /// account in the base currency have the rate 1. <c>null</c> when nothing tells (no operations).
    /// </summary>
    private static async Task<CurrencyCode?> GuessBaseCurrencyAsync(FinanceDbContext db, CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Currency, cancellationToken);
        var rates = await db.Transactions.AsNoTracking().Select(t => new { t.AccountId, t.ExchangeRate }).ToListAsync(cancellationToken);
        var counts = rates.Where(r => r.ExchangeRate == 1m && accounts.ContainsKey(r.AccountId))
            .GroupBy(r => accounts[r.AccountId])
            .Select(g => (Currency: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ToList();
        return counts.Count == 0 ? null : counts[0].Currency;
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
