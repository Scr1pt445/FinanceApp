using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

/// <summary>«Ещё → Валюты».</summary>
public partial class CurrenciesPage : ContentPage
{
    private readonly CurrenciesViewModel _viewModel;

    public CurrenciesPage(CurrenciesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnBack(object? sender, EventArgs e) => await Navigation.PopAsync();

    private async void OnAdd(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CurrencyRow row)
        {
            await _viewModel.AddCommand.ExecuteAsync(row);
        }
    }

    private async void OnRemove(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CurrencyRow row)
        {
            await _viewModel.RemoveCommand.ExecuteAsync(row);
        }
    }
}
