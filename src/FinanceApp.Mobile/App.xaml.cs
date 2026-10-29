using FinanceApp.Presentation.Home;

namespace FinanceApp.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly HomePage _home;
    private readonly HomeViewModel _viewModel;

    public App(HomePage home, HomeViewModel viewModel)
    {
        InitializeComponent();

        // Dark only, like the desktop app: the palette is designed for it.
        UserAppTheme = AppTheme.Dark;
        _home = home;
        _viewModel = viewModel;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(_home) { Title = "FinanceApp" };

    /// <summary>Back from the background, maybe the next day: «Сегодня» and the totals must be current.</summary>
    protected override void OnResume()
    {
        base.OnResume();
        _ = _viewModel.RefreshAsync();
    }
}
