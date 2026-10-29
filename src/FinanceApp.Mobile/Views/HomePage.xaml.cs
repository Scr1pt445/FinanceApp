using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;

namespace FinanceApp.Mobile.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;
    private readonly ShellViewModel _shell;
    private bool _opened;

    public HomePage(HomeViewModel viewModel, ShellViewModel shell)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _shell = shell;
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

        // Recording is the point of the app: the keyboard is up as soon as the app is.
        Line.Focus();
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
}
