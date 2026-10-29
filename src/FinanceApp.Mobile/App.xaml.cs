using FinanceApp.Presentation.Home;

namespace FinanceApp.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider _services;
    private readonly HomeViewModel _viewModel;

    // The page is not a constructor parameter: DI would build it before InitializeComponent loads the theme,
    // and its {StaticResource} lookups would fail — the app crashed on start that way.
    public App(IServiceProvider services, HomeViewModel viewModel)
    {
        InitializeComponent();

        // Dark only, like the desktop app: the palette is designed for it.
        UserAppTheme = AppTheme.Dark;
        _services = services;
        _viewModel = viewModel;
    }

    /// <summary>A new page for every window: Android may recreate the activity, and a page can have only one parent.</summary>
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<HomePage>()) { Title = "FinanceApp" };

    /// <summary>Back from the background, maybe the next day: «Сегодня» and the totals must be current.</summary>
    protected override void OnResume()
    {
        base.OnResume();
        _ = _viewModel.RefreshAsync();
    }
}
