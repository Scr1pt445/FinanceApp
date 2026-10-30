using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

public partial class AccountsPage : ContentPage
{
    private readonly ShellViewModel _shell;

    public AccountsPage(ShellViewModel shell)
    {
        InitializeComponent();
        BindingContext = _shell = shell;

        // The chips are rebuilt when the list changes and must show the choice again; so must a new choice.
        shell.TotalsCurrencies.CollectionChanged += (_, _) => HighlightCurrency();
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.TotalsCurrency))
            {
                HighlightCurrency();
            }
        };
        HighlightCurrency();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _shell.RefreshAsync();
    }

    private void OnCurrency(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CurrencyCode currency)
        {
            _shell.TotalsCurrency = currency;
        }
    }

    private void HighlightCurrency() => Controls.Chips.Highlight(Currencies, _shell.TotalsCurrency);

    /// <summary>An account or envelope opens its operations, where it can also be edited, filled or emptied.</summary>
    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SidebarItem item)
        {
            _shell.Selected = item;
            (Shell.Current as AppShell)?.Show(AppShell.OperationsRoute);
        }
    }
}
