using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

public partial class CategoriesPage : ContentPage
{
    private readonly CategoriesViewModel _viewModel;

    public CategoriesPage(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        InitializeComponent();
        BindingContext = _viewModel = shell.Categories;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnBack(object? sender, EventArgs e) => await Navigation.PopAsync();
}
