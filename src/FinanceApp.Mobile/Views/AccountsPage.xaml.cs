using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

public partial class AccountsPage : ContentPage
{
    private readonly ShellViewModel _shell;

    public AccountsPage(ShellViewModel shell)
    {
        InitializeComponent();
        BindingContext = _shell = shell;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _shell.RefreshAsync();
    }

    /// <summary>An account or envelope opens its operations, where it can also be edited, filled or emptied.</summary>
    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SidebarItem item)
        {
            _shell.Selected = item;
            await Shell.Current.GoToAsync("//" + AppShell.OperationsRoute);
        }
    }
}
