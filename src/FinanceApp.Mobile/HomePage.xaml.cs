using FinanceApp.Presentation.Home;

namespace FinanceApp.Mobile;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;
    private readonly AppStartup _startup;

    public HomePage(HomeViewModel viewModel, AppStartup startup)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _startup = startup;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _startup.EnsureReadyAsync();
        }
#pragma warning disable CA1031 // Without a database there is nothing to show; say why instead of crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await DisplayAlertAsync("FinanceApp", $"Не удалось открыть базу данных: {ex.Message}", "OK");
            return;
        }

        await _viewModel.OpenAsync();

        // Recording is the point of the app: the keyboard is up as soon as the screen is.
        Line.Focus();
    }
}
