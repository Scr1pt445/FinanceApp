using FinanceApp.Domain;
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
        form.PaidCurrencies.CollectionChanged += (_, _) => HighlightPaidCurrency();
        form.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TransactionFormViewModel.PaidCurrency))
            {
                HighlightPaidCurrency();
            }
            else if (e.PropertyName == nameof(TransactionFormViewModel.Remember))
            {
                HighlightRemember();
            }
            else if (e.PropertyName == nameof(TransactionFormViewModel.Repeat))
            {
                Controls.Chips.Highlight(RepeatChips, _form.Repeat);
            }
        };
        HighlightPaidCurrency();
        HighlightRemember();
        Controls.Chips.Highlight(RepeatChips, _form.Repeat);
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

    private void OnPaidCurrency(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CurrencyCode currency)
        {
            _form.ChoosePaidCurrencyCommand.Execute(currency);
        }
    }

    private void OnRepeat(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is RepeatChoice choice)
        {
            _form.ChooseRepeatCommand.Execute(choice);
        }
    }

    private void OnRemember(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is RememberChoice choice)
        {
            _form.ChooseRememberCommand.Execute(choice);
        }
    }

    private void HighlightRemember()
    {
        Controls.Chips.Highlight(RememberForeign, _form.Remember);
        Controls.Chips.Highlight(RememberTransfer, _form.Remember);
    }

    private void HighlightPaidCurrency() => Controls.Chips.Highlight(PaidCurrencies, _form.PaidCurrency);

    private void OnTileTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CategoryTile tile)
        {
            _form.ChooseCategoryCommand.Execute(tile);
        }
    }
}
