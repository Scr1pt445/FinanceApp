using FinanceApp.Application.Backups;

namespace FinanceApp.Presentation.Data;

/// <summary>What the «Данные» screen needs from the core: files to hand over to other apps.</summary>
public interface IDataBackend
{
    /// <summary>Writes <c>transactions.csv</c> and <c>accounts.csv</c> (ADR 0011) into the folder.</summary>
    Task<ExportedCsv> ExportCsvAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>Makes a copy of the database now (ADR 0012).</summary>
    Task<BackupInfo> BackupAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<BackupInfo> ListBackups();

    /// <summary>What a copy holds; throws <see cref="InvalidDataException"/> for a file that is not one.</summary>
    Task<BackupContents> InspectBackupAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Replaces the data with the copy; the current data goes to the backups first.</summary>
    Task<RestoreResult> RestoreAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>The platform's way to pick a file another app keeps (Downloads, a cloud drive, a messenger).</summary>
public interface IFileChooser
{
    /// <summary>A local path to read the chosen file from, or <c>null</c> when nothing was chosen.</summary>
    Task<string?> PickAsync(string title);
}

/// <summary>The written files and how many transactions they hold.</summary>
public sealed record ExportedCsv(int Rows, IReadOnlyList<string> Paths);

/// <summary>The platform's way to hand files to other apps (the Android share sheet).</summary>
public interface IFileSharer
{
    /// <summary>A folder for files that are about to be shared; the platform may clean it up.</summary>
    string ShareFolder { get; }

    Task ShareAsync(string title, IReadOnlyList<string> paths);
}
