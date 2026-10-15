using System.Diagnostics;
using System.Windows;
using FinanceApp.Application.Export;
using FinanceApp.Application.Settings;
using FinanceApp.Desktop.Interop;
using FinanceApp.Desktop.Tray;
using FinanceApp.Infrastructure;
using FinanceApp.Infrastructure.Persistence;
using FinanceApp.Presentation.Main;
using FinanceApp.Presentation.QuickEntry;
using FinanceApp.Presentation.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Desktop;

/// <summary>
/// Composition root of the desktop app. No main window yet (milestone 3): the app lives in the tray
/// and opens the quick-entry window on the global hotkey.
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageBox.Show(args.Exception.Message, "FinanceApp — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        _singleInstance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(OpenQuickEntry));
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
                .AddSingleton<IMainBackend, ScopedMainBackend>()
                .AddSingleton<MainViewModel>()
                .AddSingleton<MainWindow>()
                .BuildServiceProvider(validateScopes: true);

            var settings = _services.GetRequiredService<ISettingsStore>();
            await using (var scope = _services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(settings.Load().BaseCurrency);
            }

            _quickEntry = _services.GetRequiredService<QuickEntryWindow>();
            _main = _services.GetRequiredService<MainWindow>();
            _main.QuickEntryRequested += (_, _) => OpenQuickEntry();
            _services.GetRequiredService<QuickEntryViewModel>().Saved += (_, saved) =>
            {
                _tray?.SetStatus("Сохранено: " + QuickEntryFormatter.Saved(saved, settings.Load().BaseCurrency));
                _main.RefreshIfVisible();
            };

            _tray = new TrayIcon(OpenMain, OpenQuickEntry, () => OpenHotKeySettings(null), () => OpenDataFolder(paths), ExitApp);
            WatchCsvExport();

            _hotKeys = _services.GetRequiredService<HotKeyService>();
            _hotKeys.Pressed += (_, _) => OpenQuickEntry();
            _hotKeys.Changed += (_, _) => ShowHotKey(_hotKeys.Current?.ToString());

            var startupError = RegisterHotKeyFromSettings(settings.Load());
            ShowHotKey(_hotKeys.Current?.ToString());
            if (startupError is not null)
            {
                // Without a hotkey the app is easy to lose in the tray: ask for another combination right away.
                OpenHotKeySettings(startupError);
            }
            else if (e.Args.Contains("--main", StringComparer.OrdinalIgnoreCase))
            {
                OpenMain();
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

        // Disposes the hotkey and flushes a pending CSV export before the process ends.
        _services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
        _main?.SetHotKeyHint(hotKey);
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
        _hotKeyWindow.Closed += (_, _) => _hotKeyWindow = null;
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

    private static void OpenDataFolder(FinancePaths paths) =>
        Process.Start(new ProcessStartInfo(paths.DataDirectory) { UseShellExecute = true })?.Dispose();
}
