using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

public partial class SummaryPage : ContentPage
{
    private readonly SummaryViewModel _viewModel;

    public SummaryPage(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        InitializeComponent();
        BindingContext = _viewModel = shell.Summary;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
