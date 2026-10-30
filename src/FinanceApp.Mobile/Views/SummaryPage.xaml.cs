using System.ComponentModel;
using FinanceApp.Mobile.Controls;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

public partial class SummaryPage : ContentPage
{
    private readonly SummaryViewModel _viewModel;

    public SummaryPage(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        InitializeComponent();
        BindingContext = _viewModel = shell.Summary;
        _viewModel.PropertyChanged += OnSummaryChanged;
        Chips.Highlight(Periods, _viewModel.Choice);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private void OnPeriod(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is PeriodChoice choice)
        {
            _viewModel.Choice = choice;
        }
    }

    private void OnSummaryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SummaryViewModel.Choice))
        {
            Chips.Highlight(Periods, _viewModel.Choice);
        }
    }
}
