using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;
using FinanceApp.Application.Settings;
using FinanceApp.Presentation.Settings;
using FinanceApp.Presentation.Setup;

namespace FinanceApp.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider _services;
    private readonly AppStartup _startup;
    private readonly HomeViewModel _home;
    private readonly RatePrefetcher _rates;
    private readonly LockViewModel _lock;
    private AppShell? _shell;
    private LockPage? _lockPage;
    private DateTimeOffset? _sleptAt;

    // Pages are not constructor parameters: DI would build them before InitializeComponent loads the theme,
    // and their {StaticResource} lookups would fail — the first APK crashed on start that way.
    /// <summary>Back after this long in the background, the app asks for the PIN again (a file picker or a share sheet is shorter).</summary>
    public static readonly TimeSpan LockAfter = TimeSpan.FromSeconds(60);

    public App(IServiceProvider services, AppStartup startup, HomeViewModel home, RatePrefetcher rates, LockViewModel lockViewModel)
    {
        InitializeComponent();

        // Dark only, like the desktop app: the palette is designed for it.
        UserAppTheme = AppTheme.Dark;
        _services = services;
        _startup = startup;
        _home = home;
        _rates = rates;
        _lock = lockViewModel;
        _lock.Unlocked += (_, _) => _ = UnlockedAsync();
        PendingAction.Requested += (_, _) => Dispatcher.Dispatch(RunPendingAction);

        // The home-screen widget shows what the home screen counted last.
        home.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(HomeViewModel.TotalText) or nameof(HomeViewModel.TotalDetails) or nameof(HomeViewModel.MonthText))
            {
                PublishWidget();
            }
        };
    }

    /// <summary>The PIN is asked for: on start and after <see cref="LockAfter"/> in the background.</summary>
    public bool IsLocked => _lockPage is not null;

    public void PublishWidget()
    {
        var lockSettings = _services.GetRequiredService<ISettingsStore>().Load().Lock;
        WidgetData.Publish(WidgetText.From(_home.TotalText, _home.TotalDetails, _home.MonthText, lockSettings.IsOn && !lockSettings.WidgetAmounts));
    }

    /// <summary>
    /// The tabs appear once the database is ready: their view models start loading as soon as they exist,
    /// and on the first run the tables are not there yet.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var startup = new StartupPage();
        var window = new Window(startup) { Title = "FinanceApp" };
        if (_startup.NeedsSetup)
        {
            // A fresh install: the tour and the base currency first; the database is created with that choice.
            var setup = SetupViewModel.ForFirstRun(_services.GetRequiredService<ISetupBackend>());
            setup.Completed += (_, _) => window.Dispatcher.Dispatch(() =>
            {
                window.Page = startup;
                _ = ShowAppAsync(window, startup);
            });
            window.Page = new SetupPage(setup);
            return window;
        }

        _ = ShowAppAsync(window, startup);
        return window;
    }

    /// <summary>Back from the background, maybe the next day: «Сегодня» and the totals must be current.</summary>
    protected override void OnResume()
    {
        base.OnResume();
        if (_sleptAt is { } slept && DateTimeOffset.UtcNow - slept >= LockAfter)
        {
            _ = ShowLockAsync();
        }

        _sleptAt = null;
        _ = _home.RefreshAsync();
        _ = RefreshRatesAsync();
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        _sleptAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The lock over everything, forms included; nothing under it can be touched.</summary>
    private async Task ShowLockAsync()
    {
        if (!_lock.IsOn || _shell is null || _lockPage is not null)
        {
            return;
        }

        try
        {
            _lockPage = new LockPage(_lock, _services.GetRequiredService<IBiometrics>());
            await _shell.Navigation.PushModalAsync(_lockPage, animated: false);
        }
#pragma warning disable CA1031 // Without the lock screen the app must not crash; the report says why.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }
    }

    private async Task UnlockedAsync()
    {
        if (_lockPage is not { } page || _shell is null)
        {
            return;
        }

        _lockPage = null;
        try
        {
            var modals = _shell.Navigation.ModalStack;
            if (modals.Count > 0 && ReferenceEquals(modals[^1], page))
            {
                await _shell.Navigation.PopModalAsync(animated: true);
            }
        }
#pragma warning disable CA1031 // See ShowLockAsync.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }

        RunPendingAction();
    }

    /// <summary>«Новый расход» from a shortcut or the widget, once the tabs are there and the lock is open.</summary>
    private void RunPendingAction()
    {
        if (_shell is null || IsLocked || PendingAction.Take() is not { } kind)
        {
            return;
        }

        var shell = _services.GetRequiredService<ShellViewModel>();
        var command = kind switch
        {
            PendingAction.Income => shell.NewIncomeCommand,
            PendingAction.Transfer => shell.NewTransferCommand,
            _ => shell.NewExpenseCommand,
        };
        _ = command.ExecuteAsync(null);
    }

    /// <summary>In the background: a new day's rates, then the totals once more with them.</summary>
    private async Task RefreshRatesAsync()
    {
        try
        {
            if (await _rates.WarmUpAsync())
            {
                await _home.RefreshAsync();
                await _services.GetRequiredService<ShellViewModel>().RefreshAsync();
            }
        }
#pragma warning disable CA1031 // Rates are a nicety here; the screens work with the cached ones.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private async Task ShowAppAsync(Window window, StartupPage startup)
    {
        try
        {
            await _startup.EnsureReadyAsync();
        }
#pragma warning disable CA1031 // Without a database there is nothing to show; say why instead of crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            startup.ShowError($"Не удалось открыть базу данных: {ex.Message}");
            return;
        }

        window.Dispatcher.Dispatch(() =>
        {
            try
            {
                var shell = _services.GetRequiredService<AppShell>();
                window.Page = shell;
                _shell = shell;

                // Only now: the view models it listens to load data as soon as they exist.
                _services.GetRequiredService<Screens>();
                _services.GetRequiredService<FormPresenter>().Attach(shell.Navigation);
                _ = RefreshRatesAsync();
                PublishWidget();
                if (_lock.IsOn)
                {
                    _ = ShowLockAsync();
                }
                else
                {
                    RunPendingAction();
                }
            }
#pragma warning disable CA1031 // A screen that cannot be built is reported in place, not as a silent blank window.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                CrashReport.Save(ex);
                startup.ShowError($"Не удалось открыть приложение: {ex.Message}");
            }
        });
    }
}
