using System.ComponentModel;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

/// <summary>«Ещё → Регулярные операции».</summary>
public partial class RecurringPage : ContentPage
{
    private readonly RecurringViewModel _viewModel;
    private readonly ShellViewModel _shell;

    public RecurringPage(RecurringViewModel viewModel, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _shell = shell;
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
        if (e.PropertyName == nameof(RecurringViewModel.Editor) && _viewModel.Editor is { } editor)
        {
            Controls.Chips.Highlight(FrequencyChips, editor.Frequency);
            editor.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(RecurringEditor.Frequency))
                {
                    Controls.Chips.Highlight(FrequencyChips, editor.Frequency);
                }
            };
        }
    }

    private async void OnBack(object? sender, EventArgs e) => await Navigation.PopAsync();

    /// <summary>A new one is an ordinary operation with «Повторять»: the form opens over the tabs.</summary>
    private async void OnNew(object? sender, EventArgs e) => await _shell.NewRecurringCommand.ExecuteAsync(null);

    private void OnItem(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is RecurringItem item)
        {
            _viewModel.EditCommand.Execute(item);
        }
    }

    private void OnFrequency(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is RepeatChoice choice)
        {
            _viewModel.Editor?.ChooseFrequencyCommand.Execute(choice);
        }
    }

    private void OnEditorBackdrop(object? sender, TappedEventArgs e) => _viewModel.Editor?.CancelCommand.Execute(null);
}
