using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Home;

namespace FinanceApp.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider _services;
    private readonly AppStartup _startup;
    private readonly HomeViewModel _home;

    // Pages are not constructor parameters: DI would build them before InitializeComponent loads the theme,
    // and their {StaticResource} lookups would fail — the first APK crashed on start that way.
    public App(IServiceProvider services, AppStartup startup, HomeViewModel home)
    {
        InitializeComponent();

        // Dark only, like the desktop app: the palette is designed for it.
        UserAppTheme = AppTheme.Dark;
        _services = services;
        _startup = startup;
        _home = home;
    }

    /// <summary>
    /// The tabs appear once the database is ready: their view models start loading as soon as they exist,
    /// and on the first run the tables are not there yet.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var startup = new StartupPage();
        var window = new Window(startup) { Title = "FinanceApp" };
        _ = ShowAppAsync(window, startup);
        return window;
    }

    /// <summary>Back from the background, maybe the next day: «Сегодня» and the totals must be current.</summary>
    protected override void OnResume()
    {
        base.OnResume();
        _ = _home.RefreshAsync();
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
            var shell = _services.GetRequiredService<AppShell>();
            window.Page = shell;
            _services.GetRequiredService<FormPresenter>().Attach(shell.Navigation);
        });
    }
}
