using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FinanceApp.Application.Export;
using FinanceApp.Application.Settings;
using FinanceApp.Desktop.Interop;
using FinanceApp.Desktop.Tray;
using FinanceApp.Infrastructure;
using FinanceApp.Infrastructure.Persistence;
using FinanceApp.Presentation.QuickEntry;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Desktop;

/// <summary>
/// Composition root of the desktop app. No main window yet (milestone 3): the app lives in the tray
/// and opens the quick-entry window on the global hotkey.
/// </summary>
public partial class App
{
    public const ModifierKeys HotKeyModifiers = ModifierKeys.Control | ModifierKeys.Alt;
    public const Key HotKeyKey = Key.M;

    private SingleInstance? _singleInstance;
    private ServiceProvider? _services;
    private GlobalHotKey? _hotKey;
    private TrayIcon? _tray;
    private QuickEntryWindow? _quickEntry;

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
                .BuildServiceProvider(validateScopes: true);

            await using (var scope = _services.CreateAsyncScope())
            {
                var settings = _services.GetRequiredService<ISettingsStore>().Load();
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(settings.BaseCurrency);
            }

            _quickEntry = _services.GetRequiredService<QuickEntryWindow>();
            var viewModel = _services.GetRequiredService<QuickEntryViewModel>();
            viewModel.Saved += (_, saved) => _tray?.SetStatus(
                "Сохранено: " + QuickEntryFormatter.Saved(saved, _services.GetRequiredService<ISettingsStore>().Load().BaseCurrency));

            var hotKeyName = RegisterHotKey();
            _tray = new TrayIcon(hotKeyName, OpenQuickEntry, () => OpenDataFolder(paths), Shutdown);
            WatchCsvExport();

            if (_hotKey is null)
            {
                _tray.Notify("Горячая клавиша занята", $"{hotKeyName} уже использует другое приложение. Быстрый ввод — по клику на значок в трее.", isWarning: true);
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
        _hotKey?.Dispose();
        _tray?.Dispose();

        // Flushes a pending CSV export before the process ends.
        _services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private string RegisterHotKey()
    {
        try
        {
            _hotKey = GlobalHotKey.Register(HotKeyModifiers, HotKeyKey);
            _hotKey.Pressed += (_, _) => OpenQuickEntry();
            return _hotKey.DisplayName;
        }
        catch (Win32Exception)
        {
            return "Ctrl+Alt+M";
        }
    }

    private void OpenQuickEntry() => _quickEntry?.ShowAndFocus();

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
