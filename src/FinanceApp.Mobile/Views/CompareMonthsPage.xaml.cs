using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

/// <summary>«Сравнение месяцев», opened from «Итоги» and from «Ещё».</summary>
public partial class CompareMonthsPage : ContentPage
{
    private readonly CompareMonthsViewModel _viewModel;

    public CompareMonthsPage(CompareMonthsViewModel viewModel)
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
}
