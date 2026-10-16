using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Backups;
using FinanceApp.Application.Export;
using FinanceApp.Application.Settings;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Presentation.Settings;

/// <summary>A copy in the «Резервные копии» list: «16 октября 2026, 09:30 · 1,2 МБ».</summary>
public sealed record BackupRow(string Title, string SizeText, string Path);

/// <summary>
/// «Настройки»: quick-entry hotkey, start with Windows, CSV export for Obsidian or spreadsheets, database
/// backups and where the data lives. Switches apply at once; the export path is applied with a button
/// because a half-typed path must not be written to.
/// </summary>
public sealed partial class SettingsViewModel(
    ISettingsBackend backend,
    IDatabaseBackup backups,
    IAutostart autostart,
    IHotKeyRegistrar hotKeys,
    TimeProvider timeProvider) : ObservableObject
{
    private bool _loading;

    [ObservableProperty]
    public partial string HotKeyText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool ExportEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportProblem), nameof(CanApplyExport))]
    public partial string ExportPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExportStatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool ExportFailed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyExport))]
    public partial bool IsExporting { get; private set; }

    [ObservableProperty]
    public partial bool BackupsEnabled { get; set; }

    [ObservableProperty]
    public partial string KeepText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BackupStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool BackupFailed { get; private set; }

    public ObservableCollection<BackupRow> Backups { get; } = [];

    public string DataDirectory => backend.DataDirectory;

    public string VersionText => $"FinanceApp {backend.Version}";

    public string BackupFolder => backups.Folder;

    [ObservableProperty]
    public partial string BaseCurrencyText { get; private set; } = string.Empty;

    /// <summary>Why the typed export path cannot be used, or <c>null</c>.</summary>
    public string? ExportProblem =>
        !ExportEnabled ? null
        : string.IsNullOrWhiteSpace(ExportPath) ? "Укажите файл, например C:\\Notes\\Finance\\transactions.csv."
        : !IsAbsolute(ExportPath) ? "Нужен полный путь, с диском: C:\\…"
        : !ExportPath.Trim().EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "Файл должен быть .csv."
        : null;

    public bool CanApplyExport => ExportProblem is null && !IsExporting;

    /// <summary>The view opens the existing «Горячая клавиша» window.</summary>
    public event EventHandler? ChangeHotKeyRequested;

    /// <summary>The view opens a folder in Explorer.</summary>
    public event EventHandler<string>? OpenFolderRequested;

    public void Load()
    {
        _loading = true;
        try
        {
            var settings = backend.Load();
            HotKeyText = hotKeys.Current?.ToString() ?? "не назначена";
            StartWithWindows = autostart.IsEnabled;
            ExportEnabled = settings.CsvExport.Enabled;
            ExportPath = settings.CsvExport.Path ?? string.Empty;
            ShowExportStatus(backend.ExportStatus, settings.CsvExport.IsActive);
            BackupsEnabled = settings.Backups.Enabled;
            KeepText = settings.Backups.Keep.ToString(CultureInfo.InvariantCulture);
            BaseCurrencyText = settings.BaseCurrency.Value;
            RefreshBackups();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Called after the «Горячая клавиша» window closes.</summary>
    public void RefreshHotKey() => HotKeyText = hotKeys.Current?.ToString() ?? "не назначена";

    [RelayCommand]
    private void ChangeHotKey() => ChangeHotKeyRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenDataFolder() => OpenFolderRequested?.Invoke(this, DataDirectory);

    [RelayCommand]
    private void OpenBackupFolder()
    {
        Directory.CreateDirectory(BackupFolder);
        OpenFolderRequested?.Invoke(this, BackupFolder);
    }

    /// <summary>Saves the export settings and, when on, writes the file right away so the user sees it works.</summary>
    [RelayCommand]
    private async Task ApplyExportAsync(CancellationToken cancellationToken)
    {
        if (!CanApplyExport)
        {
            return;
        }

        var settings = backend.Load();
        var path = ExportEnabled ? Path.GetFullPath(ExportPath.Trim()) : settings.CsvExport.Path;
        await backend.SaveAsync(settings with { CsvExport = new CsvExportSettings { Enabled = ExportEnabled, Path = path } }, cancellationToken);
        ExportPath = path ?? string.Empty;

        if (!ExportEnabled)
        {
            ShowExportStatus(CsvExportStatus.Idle, active: false);
            return;
        }

        IsExporting = true;
        ExportStatusText = "Записываю…";
        try
        {
            ShowExportStatus(await backend.ExportNowAsync(cancellationToken), active: true);
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task BackupNowAsync(CancellationToken cancellationToken)
    {
        try
        {
            var copy = await backups.CreateAsync(cancellationToken);
            BackupFailed = false;
            BackupStatus = $"Копия сделана: {Path.GetFileName(copy.Path)}";
        }
#pragma warning disable CA1031 // Shown in the window: disk full, folder not writable…
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            BackupFailed = true;
            BackupStatus = $"Не удалось сделать копию: {ex.Message}";
        }

        RefreshBackups();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            autostart.SetEnabled(value);
        }
#pragma warning disable CA1031 // A policy may forbid it; show the real state.
        catch (Exception)
#pragma warning restore CA1031
        {
            _loading = true;
            StartWithWindows = autostart.IsEnabled;
            _loading = false;
        }
    }

    partial void OnExportEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ExportProblem));
        OnPropertyChanged(nameof(CanApplyExport));

        // Turning export off needs no path, so it is applied at once; turning it on waits for «Применить».
        if (!_loading && !value)
        {
            _ = ApplyExportAsync(CancellationToken.None);
        }
    }

    partial void OnBackupsEnabledChanged(bool value) => _ = SaveBackupsAsync();

    partial void OnKeepTextChanged(string value) => _ = SaveBackupsAsync();

    private async Task SaveBackupsAsync()
    {
        if (_loading)
        {
            return;
        }

        if (!int.TryParse(KeepText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var keep) || keep is < 1 or > BackupSettings.MaxKeep)
        {
            BackupFailed = true;
            BackupStatus = $"Сколько копий хранить — число от 1 до {BackupSettings.MaxKeep}.";
            return;
        }

        var settings = backend.Load();
        await backend.SaveAsync(settings with { Backups = new BackupSettings { Enabled = BackupsEnabled, Keep = keep } });
        BackupFailed = false;
        BackupStatus = BackupsEnabled ? $"Каждый день при запуске; хранятся последние {keep}." : "Ежедневные копии выключены.";
    }

    private void ShowExportStatus(CsvExportStatus status, bool active)
    {
        ExportFailed = status.State == CsvExportState.Failed;
        ExportStatusText = !active ? "Экспорт выключен."
            : status.State switch
            {
                CsvExportState.Succeeded =>
                    $"Записано {status.Rows} {Rows(status.Rows)} в {status.LastSuccessUtc!.Value.ToLocalTime().ToString("HH:mm", Format.Russian)}; файл обновляется после каждого изменения.",
                CsvExportState.Failed => status.Error ?? "Не удалось записать файл.",
                _ => "Файл обновляется после каждого изменения.",
            };
    }

    private void RefreshBackups()
    {
        Backups.Clear();
        var today = timeProvider.GetLocalNow().Date;
        foreach (var copy in backups.List())
        {
            var day = copy.CreatedLocal.Date == today ? "Сегодня" : copy.CreatedLocal.ToString("d MMMM yyyy", Format.Russian);
            Backups.Add(new BackupRow($"{day}, {copy.CreatedLocal.ToString("HH:mm", Format.Russian)}", Size(copy.SizeBytes), copy.Path));
        }

        if (!BackupFailed && BackupStatus.Length == 0)
        {
            BackupStatus = Backups.Count == 0 ? "Копий пока нет." : string.Empty;
        }
    }

    private static bool IsAbsolute(string path)
    {
        var trimmed = path.Trim();
        return Path.IsPathFullyQualified(trimmed) && trimmed.IndexOfAny(Path.GetInvalidPathChars()) < 0;
    }

    private static string Rows(int count) => (count % 10, count % 100) switch
    {
        (1, not 11) => "строка",
        (2 or 3 or 4, < 12 or > 14) => "строки",
        _ => "строк",
    };

    private static string Size(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / 1024d / 1024d).ToString("0.0", Format.Russian) + " МБ"
            : Math.Max(1, bytes / 1024).ToString(Format.Russian) + " КБ";
}
