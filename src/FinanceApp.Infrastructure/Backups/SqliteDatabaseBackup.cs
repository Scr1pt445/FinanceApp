using System.Globalization;
using FinanceApp.Application.Backups;
using FinanceApp.Application.Settings;
using Microsoft.Data.Sqlite;

namespace FinanceApp.Infrastructure.Backups;

/// <summary>
/// <c>VACUUM INTO</c> copies of <c>finance.db</c> into <c>backups\finance-yyyy-MM-dd_HHmmss.db</c>: a consistent,
/// compacted snapshot taken through SQLite itself, so it is safe while the app is writing (ADR 0012).
/// </summary>
public sealed class SqliteDatabaseBackup(FinancePaths paths, ISettingsStore settings, TimeProvider timeProvider) : IDatabaseBackup
{
    private const string Prefix = "finance-";
    private const string StampFormat = "yyyy-MM-dd_HHmmss";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Folder => Path.Combine(paths.DataDirectory, "backups");

    public IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(Folder, Prefix + "*.db")
            .Select(Describe)
            .OfType<BackupInfo>()
            .OrderByDescending(b => b.CreatedLocal)
            .ToList();
    }

    public async Task<BackupInfo> CreateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Folder);
            var now = timeProvider.GetLocalNow().DateTime;
            var path = Path.Combine(Folder, Prefix + now.ToString(StampFormat, CultureInfo.InvariantCulture) + ".db");

            // Write next to the target first: a half-written copy must never look like a backup.
            var partial = path + ".partial";
            File.Delete(partial);
            await using (var connection = new SqliteConnection(paths.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $path";
                command.Parameters.AddWithValue("$path", partial);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            File.Move(partial, path, overwrite: true);
            Prune(settings.Load().Backups.Keep);
            return Describe(path)!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackupInfo?> EnsureDailyAsync(CancellationToken cancellationToken = default)
    {
        if (!settings.Load().Backups.Enabled || !File.Exists(paths.DatabasePath))
        {
            return null;
        }

        var today = timeProvider.GetLocalNow().Date;
        return List().Any(b => b.CreatedLocal.Date == today) ? null : await CreateAsync(cancellationToken);
    }

    private void Prune(int keep)
    {
        foreach (var old in List().Skip(Math.Clamp(keep, 1, BackupSettings.MaxKeep)))
        {
            try
            {
                File.Delete(old.Path);
            }
            catch (IOException)
            {
                // Opened by someone (an explorer preview): try again next time.
            }
        }
    }

    private static BackupInfo? Describe(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!DateTime.TryParseExact(name[Prefix.Length..], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
        {
            return null;
        }

        return new BackupInfo(path, created, new FileInfo(path).Length);
    }
}
