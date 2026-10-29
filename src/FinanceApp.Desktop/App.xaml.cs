using System.Diagnostics;
using System.Windows;
using FinanceApp.Application.Backups;
using FinanceApp.Application.Export;
using FinanceApp.Application.Settings;
using FinanceApp.Desktop.Interop;
using FinanceApp.Desktop.Tray;
using FinanceApp.Infrastructure;
using FinanceApp.Infrastructure.Persistence;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Hosting;
using FinanceApp.Presentation.QuickEntry;
using FinanceApp.Presentation.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Desktop;

/// <summary>
/// Composition root of the desktop app. The app lives in the tray: the global hotkey opens quick entry,
/// the tray menu the main window and the settings.
/// </summary>
public partial class App
{
    private SingleInstance? _singleInstance;
    private ServiceProvider? _services;
    private HotKeyService? _hotKeys;
    private TrayIcon? _tray;
    private QuickEntryWindow? _quickEntry;
    private MainWindow? _main;
    private HotKeyWindow? _hotKeyWindow;
    private SettingsWindow? _settingsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageBox.Show(args.Exception.Message, "FinanceApp — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        var request = e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase) ? InstanceRequest.Exit
            : e.Args.Contains("--main", StringComparer.OrdinalIgnoreCase) ? InstanceRequest.Main
            : InstanceRequest.QuickEntry;
        _singleInstance = SingleInstance.TryAcquire(request, other => Dispatcher.BeginInvoke(() =>
        {
            switch (other)
            {
                case InstanceRequest.Exit:
                    ExitApp();
                    break;
                case InstanceRequest.Main:
                    OpenMain();
                    break;
                default:
                    OpenQuickEntry();
                    break;
            }
        }));
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        try
        {
            var paths = FinancePaths.Resolve();
            paths.EnsureDataDirectory();
            _services = new ServiceCollection()
                .AddFinanceApp(paths)
                .AddSingleton<IQuickEntryBackend, ScopedQuickEntryBackend>()
                .AddSingleton<QuickEntryViewModel>()
                .AddSingleton<QuickEntryWindow>()
                .AddSingleton<HotKeyService>()
                .AddSingleton<IHotKeyRegistrar>(sp => sp.GetRequiredService<HotKeyService>())
                .AddTransient<HotKeySettingsViewModel>()
                .AddSingleton<ISettingsBackend, DesktopSettingsBackend>()
                .AddSingleton<IAutostart, RegistryAutostart>()
                .AddTransient<SettingsViewModel>()
                .AddSingleton<IFinanceBackend, ScopedFinanceBackend>()
                .AddSingleton<ShellViewModel>()
                .AddSingleton<MainWindow>()
                .BuildServiceProvider(validateScopes: true);

            var settings = _services.GetRequiredService<ISettingsStore>();
            await using (var scope = _services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(settings.Load().BaseCurrency);
            }

            _quickEntry = _services.GetRequiredService<QuickEntryWindow>();
            _main = _services.GetRequiredService<MainWindow>();
            _main.SettingsRequested += (_, _) => OpenSettings();
            _services.GetRequiredService<QuickEntryViewModel>().Saved += (_, saved) =>
            {
                _tray?.SetStatus("Сохранено: " + QuickEntryFormatter.Saved(saved, settings.Load().BaseCurrency));
                _main.RefreshIfVisible();
            };

            _tray = new TrayIcon(OpenMain, OpenQuickEntry, OpenSettings, () => OpenFolder(paths.DataDirectory), ExitApp);
            WatchCsvExport();
            _ = MakeDailyBackupAsync();

            _hotKeys = _services.GetRequiredService<HotKeyService>();
            _hotKeys.Pressed += (_, _) => OpenQuickEntry();
            _hotKeys.Changed += (_, _) => ShowHotKey(_hotKeys.Current?.ToString());

            var startupError = RegisterHotKeyFromSettings(settings.Load());
            ShowHotKey(_hotKeys.Current?.ToString());
            if (e.Args.Contains("--main", StringComparer.OrdinalIgnoreCase))
            {
                OpenMain();
            }

            if (startupError is not null)
            {
                // Without a hotkey the app is easy to lose in the tray: ask for another combination right away (on top).
                OpenHotKeySettings(startupError);
            }
        }
#pragma warning disable CA1031 // Startup failure: tell the user and quit instead of crashing silently.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MessageBox.Show($"Не удалось запустить FinanceApp:\n{ex.Message}", "FinanceApp", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        // The hotkey belongs to a window of this thread, so it is released here. Everything else is disposed off
        // the UI thread: blocking it on async disposal deadlocks when a continuation wants to come back to it.
        _hotKeys?.Dispose();
        if (_services is { } services)
        {
            Task.Run(async () => await services.DisposeAsync()).Wait(TimeSpan.FromSeconds(10));
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <returns>Why the configured hotkey does not work, or <c>null</c> if it was registered.</returns>
    private string? RegisterHotKeyFromSettings(AppSettings settings)
    {
        if (!HotKeyGesture.TryParse(settings.QuickEntryHotKey, out var gesture))
        {
            HotKeyGesture.TryParse(AppSettings.DefaultQuickEntryHotKey, out gesture);
        }

        return _hotKeys!.TryRegister(gesture!, out var error) ? null : error;
    }

    private void ShowHotKey(string? hotKey)
    {
        _tray?.SetHotKey(hotKey);
    }

    private void OpenMain() => _main?.ShowAndRefresh();

    private void ExitApp()
    {
        if (_main is not null)
        {
            _main.IsExiting = true;
        }

        Shutdown();
    }

    private void OpenQuickEntry()
    {
        if (_hotKeyWindow is null)
        {
            _quickEntry?.ShowAndFocus();
        }
    }

    private void OpenHotKeySettings(string? startupError)
    {
        if (_hotKeyWindow is not null)
        {
            _hotKeyWindow.Activate();
            return;
        }

        _quickEntry?.Hide();
        _hotKeyWindow = new HotKeyWindow(
            _services!.GetRequiredService<HotKeySettingsViewModel>(), _hotKeys!, startupError);
        _hotKeyWindow.Closed += (_, _) =>
        {
            _hotKeyWindow = null;
            _settingsWindow?.ViewModel.RefreshHotKey();
        };
        _hotKeyWindow.Show();
        _hotKeyWindow.Activate();
    }

    private void WatchCsvExport()
    {
        var export = _services!.GetRequiredService<CsvExportCoordinator>();
        export.StatusChanged += (_, status) =>
        {
            if (status.State == CsvExportState.Failed)
            {
                Dispatcher.BeginInvoke(() => _tray?.Notify("CSV не обновлён", status.Error ?? "Ошибка записи файла.", isWarning: true));
            }
        };
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        var viewModel = _services!.GetRequiredService<SettingsViewModel>();
        viewModel.ChangeHotKeyRequested += (_, _) => OpenHotKeySettings(null);
        viewModel.OpenFolderRequested += (_, folder) => OpenFolder(folder);
        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Today's copy of the database (ADR 0012), in the background: startup does not wait for it.</summary>
    private async Task MakeDailyBackupAsync()
    {
        try
        {
            await _services!.GetRequiredService<IDatabaseBackup>().EnsureDailyAsync();
        }
#pragma warning disable CA1031 // A failed backup must not break the app; the user is told.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _tray?.Notify("Резервная копия не сделана", ex.Message, isWarning: true);
        }
    }

    private static void OpenFolder(string folder) =>
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
}
