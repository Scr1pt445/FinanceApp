using FinanceApp.Application.Export;
using FinanceApp.Application.Settings;
using FinanceApp.Infrastructure;
using FinanceApp.Presentation.Settings;
using Microsoft.Win32;

namespace FinanceApp.Desktop;

/// <summary>The settings window's view of the core: settings file, CSV export coordinator, data folder.</summary>
internal sealed class DesktopSettingsBackend(ISettingsStore store, CsvExportCoordinator export, FinancePaths paths) : ISettingsBackend
{
    public string DataDirectory => paths.DataDirectory;

    public CsvExportStatus ExportStatus => export.Status;

    public AppSettings Load() => store.Load();

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => store.SaveAsync(settings, cancellationToken);

    public Task<CsvExportStatus> ExportNowAsync(CancellationToken cancellationToken = default) => export.ExportNowAsync(cancellationToken);
}

/// <summary>
/// Start with Windows for the current user only: a value under <c>HKCU\…\Run</c> pointing at this executable
/// without arguments, so the app starts quietly in the tray. No admin rights needed.
/// </summary>
internal sealed class RegistryAutostart : IAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FinanceApp";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command && command.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к программе.");

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
