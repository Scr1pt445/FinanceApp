using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

/// <summary>Shown over the tabs while <see cref="ShellViewModel.Form"/> is open (see <see cref="FormPresenter"/>).</summary>
public partial class TransactionFormPage : ContentPage
{
    private readonly TransactionFormViewModel _form;

    public TransactionFormPage(TransactionFormViewModel form)
    {
        InitializeComponent();
        BindingContext = _form = form;
    }

    public TransactionFormViewModel Form => _form;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_form.IsNew)
        {
            Amount.Focus();
        }
    }

    /// <summary>The system Back closes the form the same way ✕ does, so the shell forgets it.</summary>
    protected override bool OnBackButtonPressed()
    {
        _form.CancelCommand.Execute(null);
        return true;
    }

    private void OnTileTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CategoryTile tile)
        {
            _form.ChooseCategoryCommand.Execute(tile);
        }
    }
}
