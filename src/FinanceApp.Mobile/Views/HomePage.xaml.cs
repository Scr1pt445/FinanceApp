using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Mobile.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;
    private readonly ShellViewModel _shell;
    private readonly Func<RatesPage> _rates;
    private bool _opened;

    public HomePage(HomeViewModel viewModel, ShellViewModel shell, PhoneSettingsViewModel settings, Func<RatesPage> rates)
    {
        _rates = rates;
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _shell = shell;

        // Whether the line is shown is known before the screen is: no flash of it for those who turned it off.
        settings.Load();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_opened)
        {
            // Back from another tab or a form: keep what is typed, catch up with what changed.
            await _viewModel.QuickEntry.ReloadContextAsync();
            await _viewModel.RefreshAsync();
            return;
        }

        _opened = true;
        await _viewModel.OpenAsync();

        // For those who chose the line, recording is the point: the keyboard is up as soon as the app is.
        if (_viewModel.QuickEntryOn)
        {
            Line.Focus();
        }
    }

    private void OnNewExpense(object? sender, EventArgs e) => _shell.NewExpenseCommand.Execute(null);

    private void OnNewIncome(object? sender, EventArgs e) => _shell.NewIncomeCommand.Execute(null);

    private void OnNewTransfer(object? sender, EventArgs e) => _shell.NewTransferCommand.Execute(null);

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is OperationRow row)
        {
            _shell.Operations.EditCommand.Execute(row);
        }
    }

    /// <summary>«Задать свой курс» under totals that miss a rate.</summary>
    private async void OnRates(object? sender, EventArgs e) => await Navigation.PushAsync(_rates());
}
