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
