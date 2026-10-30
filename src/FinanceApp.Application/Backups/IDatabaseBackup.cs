namespace FinanceApp.Application.Backups;

/// <summary>A copy of the database in the backups folder.</summary>
public sealed record BackupInfo(string Path, DateTime CreatedLocal, long SizeBytes);

/// <summary>
/// Copies of the database (ADR 0012): one a day on start, the latest <see cref="Settings.BackupSettings.Keep"/>
/// kept, more on demand. A copy is a consistent snapshot even while the app is writing.
/// </summary>
public interface IDatabaseBackup
{
    string Folder { get; }

    IReadOnlyList<BackupInfo> List();

    /// <summary>Makes a copy now and prunes old ones.</summary>
    Task<BackupInfo> CreateAsync(CancellationToken cancellationToken = default);

    /// <summary>Makes today's copy unless backups are off or there is one already.</summary>
    /// <returns>The new copy, or <c>null</c> if none was needed.</returns>
    Task<BackupInfo?> EnsureDailyAsync(CancellationToken cancellationToken = default);
}

/// <summary>What a copy holds, shown before it replaces the data.</summary>
public sealed record BackupContents(int Transactions, int Accounts, DateOnly? LastOperation);

/// <summary>The result of a restore: what is now in the app and where the replaced data was kept.</summary>
public sealed record RestoreResult(BackupContents Contents, FinanceApp.Domain.CurrencyCode BaseCurrency, BackupInfo? SafetyCopy);

/// <summary>Brings back a copy of the database (ADR 0012) — after a new phone, a reinstall or a mistake.</summary>
public interface IDatabaseRestore
{
    /// <summary>Checks that the file is a FinanceApp database and counts what it holds.</summary>
    /// <exception cref="InvalidDataException">Not a database, damaged, or not FinanceApp's.</exception>
    Task<BackupContents> InspectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the data with the copy. The current data is copied to the backups first; the copy is brought up to
    /// the current schema, and the base currency follows the copy's.
    /// </summary>
    Task<RestoreResult> RestoreAsync(string path, CancellationToken cancellationToken = default);
}
