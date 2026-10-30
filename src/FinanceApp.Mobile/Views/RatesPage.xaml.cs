using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

/// <summary>«Ещё → Курсы валют», also opened from «Задать свой курс» under totals that miss a rate.</summary>
public partial class RatesPage : ContentPage
{
    private readonly ManualRatesViewModel _viewModel;

    public RatesPage(ManualRatesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        viewModel.Currencies.CollectionChanged += (_, _) => Highlight();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ManualRatesViewModel.From) or nameof(ManualRatesViewModel.To) or nameof(ManualRatesViewModel.Period))
            {
                Highlight();
            }
        };
        Highlight();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        Highlight();
    }

    private void Highlight()
    {
        Controls.Chips.Highlight(FromChips, _viewModel.From);
        Controls.Chips.Highlight(ToChips, _viewModel.To);
        Controls.Chips.Highlight(PeriodChips, _viewModel.Period);
    }

    private async void OnBack(object? sender, EventArgs e) => await Navigation.PopAsync();

    private void OnFrom(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CurrencyCode currency)
        {
            _viewModel.ChooseFromCommand.Execute(currency);
        }
    }

    private void OnTo(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CurrencyCode currency)
        {
            _viewModel.ChooseToCommand.Execute(currency);
        }
    }

    private void OnPeriod(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is RatePeriodChoice period)
        {
            _viewModel.ChoosePeriodCommand.Execute(period);
        }
    }

    private void OnRemove(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ManualRateItem item)
        {
            _viewModel.RemoveCommand.Execute(item);
        }
    }
}
