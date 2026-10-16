using FinanceApp.Application.Export;
using FinanceApp.Application.Settings;

namespace FinanceApp.Presentation.Settings;

/// <summary>What the settings window needs from the core besides backups, autostart and the hotkey.</summary>
public interface ISettingsBackend
{
    string DataDirectory { get; }

    AppSettings Load();

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>The result of the last CSV export since the app started.</summary>
    CsvExportStatus ExportStatus { get; }

    /// <summary>Rewrites the CSV file now with the saved settings.</summary>
    Task<CsvExportStatus> ExportNowAsync(CancellationToken cancellationToken = default);
}

/// <summary>Start with Windows (the desktop app implements it for the current user).</summary>
public interface IAutostart
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
