using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FinanceApp.Presentation.Main;

namespace FinanceApp.Desktop;

/// <summary>
/// The main window. Closing it only hides it — the app keeps living in the tray.
/// Everything except keyboard plumbing is in <see cref="MainViewModel"/>.
/// </summary>
internal sealed partial class MainWindow : Window
{
    public static readonly RoutedUICommand FocusSearchCommand = new("Поиск", nameof(FocusSearchCommand), typeof(MainWindow));

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Interop.DarkTitleBar.Apply(this);
        CommandBindings.Add(new CommandBinding(FocusSearchCommand, (_, _) => SearchBox.Focus()));
    }

    /// <summary>Raised by «+ Операция».</summary>
    public event EventHandler? QuickEntryRequested;

    public bool IsExiting { get; set; }

    public void SetHotKeyHint(string? hotKey) => HotKeyHint.Text = hotKey is null ? string.Empty : $" {hotKey}";

    public async void ShowAndRefresh()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        await _viewModel.LoadAsync();
    }

    /// <summary>Reloads the month if the window is open (e.g. after a quick entry).</summary>
    public async void RefreshIfVisible()
    {
        if (IsVisible)
        {
            await _viewModel.LoadAsync();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!IsExiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void OnQuickEntry(object sender, RoutedEventArgs e) => QuickEntryRequested?.Invoke(this, EventArgs.Empty);

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowsList.SelectedItem is TransactionRow row)
        {
            _viewModel.EditCommand.Execute(row);
        }
    }

    private void OnRowsKeyDown(object sender, KeyEventArgs e)
    {
        if (RowsList.SelectedItem is not TransactionRow row)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            _viewModel.DeleteCommand.Execute(row);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _viewModel.EditCommand.Execute(row);
        }
    }
}
