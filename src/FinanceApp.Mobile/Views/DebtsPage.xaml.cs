using System.ComponentModel;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

/// <summary>«Ещё → Долги».</summary>
public partial class DebtsPage : ContentPage
{
    private readonly DebtsViewModel _viewModel;

    public DebtsPage(DebtsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_viewModel.Editor is { } editor)
        {
            editor.CancelCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DebtsViewModel.Editor) || _viewModel.Editor is not { } editor)
        {
            return;
        }

        Highlight(editor);
        editor.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(DebtEditor.Direction) or nameof(DebtEditor.Currency))
            {
                Highlight(editor);
            }
        };
    }

    private void Highlight(DebtEditor editor)
    {
        Controls.Chips.Highlight(DirectionChips, editor.Direction);
        Controls.Chips.Highlight(CurrencyChips, editor.Currency);
    }

    private async void OnBack(object? sender, EventArgs e) => await Navigation.PopAsync();

    private void OnItem(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is DebtItem item)
        {
            _viewModel.EditCommand.Execute(item);
        }
    }

    private void OnDirection(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is DebtDirectionChoice choice)
        {
            _viewModel.Editor?.ChooseDirectionCommand.Execute(choice);
        }
    }

    private void OnCurrency(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CurrencyCode currency)
        {
            _viewModel.Editor?.ChooseCurrencyCommand.Execute(currency);
        }
    }

    private async void OnDelete(object? sender, EventArgs e)
    {
        if (_viewModel.Editor is not { } editor)
        {
            return;
        }

        if (await DisplayAlertAsync("Удалить долг?", $"Запись «{editor.Title}» пропадёт совсем.", "Удалить", "Отмена"))
        {
            editor.DeleteCommand.Execute(null);
        }
    }

    private void OnEditorBackdrop(object? sender, TappedEventArgs e) => _viewModel.Editor?.CancelCommand.Execute(null);
}
