using System.ComponentModel;
using FinanceApp.Presentation.Setup;

namespace FinanceApp.Mobile.Views;

public partial class SetupPage : ContentPage
{
    private readonly SetupViewModel _setup;

    public SetupPage(SetupViewModel setup)
    {
        InitializeComponent();
        BindingContext = _setup = setup;
        setup.PropertyChanged += OnSetupChanged;
        HighlightCurrency();
    }

    public SetupViewModel Setup => _setup;

    /// <summary>The tour from «Ещё» closes with Back; the first run has nowhere to go back to.</summary>
    protected override bool OnBackButtonPressed()
    {
        if (_setup.BackCommand.CanExecute(null))
        {
            _setup.BackCommand.Execute(null);
            return true;
        }

        return !_setup.IsTourOnly;
    }

    private void OnSwipedLeft(object? sender, SwipedEventArgs e)
    {
        if (_setup.IsTour && _setup.NextCommand.CanExecute(null))
        {
            _setup.NextCommand.Execute(null);
        }
    }

    private void OnSwipedRight(object? sender, SwipedEventArgs e)
    {
        if (_setup.BackCommand.CanExecute(null))
        {
            _setup.BackCommand.Execute(null);
        }
    }

    private void OnCurrencyTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CurrencyOption option)
        {
            _setup.ChooseCurrencyCommand.Execute(option);
        }
    }

    private void OnSetupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SetupViewModel.BaseCurrency) or nameof(SetupViewModel.Index))
        {
            HighlightCurrency();
        }
    }

    /// <summary>The chosen currency's row gets the accent border and fill.</summary>
    private void HighlightCurrency()
    {
        foreach (var row in this.GetVisualTreeDescendants().OfType<Border>().Where(b => b.BindingContext is CurrencyOption))
        {
            var chosen = Equals(row.BindingContext, _setup.BaseCurrency);
            row.Stroke = chosen ? Palette.Accent : Palette.Border;
            row.BackgroundColor = chosen ? Palette.AccentSoft : Palette.Surface;
        }
    }
}
