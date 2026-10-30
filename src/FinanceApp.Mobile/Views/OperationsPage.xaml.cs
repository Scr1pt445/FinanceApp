using System.ComponentModel;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

public partial class OperationsPage : ContentPage
{
    private const string AllOperations = "Все операции";

    private readonly ShellViewModel _shell;

    public OperationsPage(ShellViewModel shell)
    {
        InitializeComponent();
        BindingContext = _shell = shell;
        shell.Operations.PropertyChanged += OnOperationsChanged;
        HighlightFilter();
    }

    public static IReadOnlyList<FilterChoice> Choices => OperationsViewModel.TypeChoices;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _shell.Operations.LoadAsync();
    }

    /// <summary>All accounts or one: the phone's version of the desktop's left column.</summary>
    private async void OnChooseAccount(object? sender, TappedEventArgs e)
    {
        var items = _shell.Accounts.Concat(_shell.Envelopes).ToList();
        var choice = await DisplayActionSheetAsync("Показать операции", "Отмена", null, [AllOperations, .. items.Select(i => i.Title)]);
        if (choice == AllOperations)
        {
            _shell.Selected = _shell.AllOperations;
        }
        else if (items.FirstOrDefault(i => i.Title == choice) is { } item)
        {
            _shell.Selected = item;
        }
    }

    private void OnFilter(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is FilterChoice choice)
        {
            _shell.Operations.TypeFilter = choice;
        }
    }

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is OperationRow row)
        {
            _shell.Operations.EditCommand.Execute(row);
        }
    }

    private void OnDelete(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is OperationRow row)
        {
            _shell.Operations.DeleteCommand.Execute(row);
        }
    }

    private void OnOperationsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationsViewModel.TypeFilter))
        {
            HighlightFilter();
        }
    }

    private void HighlightFilter() => Controls.Chips.Highlight(Filters, _shell.Operations.TypeFilter);
}
