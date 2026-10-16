using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Desktop;

/// <summary>
/// The main window. Closing it only hides it — the app keeps living in the tray.
/// Everything except mouse and keyboard plumbing is in <see cref="ShellViewModel"/>.
/// </summary>
internal sealed partial class MainWindow : Window
{
    public static readonly RoutedUICommand FocusSearchCommand = new("Поиск", nameof(FocusSearchCommand), typeof(MainWindow));

    private readonly ShellViewModel _viewModel;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Interop.DarkTitleBar.Apply(this);
        CommandBindings.Add(new CommandBinding(FocusSearchCommand, (_, _) => SearchBox.Focus()));
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public bool IsExiting { get; set; }

    /// <summary>«Настройки» in the sidebar; the app opens the settings window.</summary>
    public event EventHandler? SettingsRequested;

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public async void ShowAndRefresh()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        await _viewModel.RefreshAsync();
    }

    /// <summary>Reloads balances and the current page if the window is open (e.g. after a quick entry).</summary>
    public async void RefreshIfVisible()
    {
        if (IsVisible)
        {
            await _viewModel.RefreshAsync();
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

    private void OnSidebarClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SidebarItem item })
        {
            _viewModel.Selected = item;
        }
    }

    private void OnSidebarDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SidebarItem { Account: not null } item })
        {
            _viewModel.EditAccountCommand.Execute(item);
        }
    }

    private void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OperationRow row })
        {
            return;
        }

        _viewModel.Operations.Selected = row;
        if (e.ClickCount == 2)
        {
            _viewModel.Operations.EditCommand.Execute(row);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Forms: Esc closes (IsCancel buttons handle it); the list keys below must not fire under a form.
        if (_viewModel.HasForm || Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (e.Key == Key.Delete && _viewModel.Operations.Selected is { } row)
        {
            e.Handled = true;
            _ = _viewModel.Operations.DeleteCommand.ExecuteAsync(row);
        }
        else if (e.Key == Key.Enter && _viewModel.Operations.Selected is { } selected)
        {
            e.Handled = true;
            _viewModel.Operations.EditCommand.Execute(selected);
        }
    }

    /// <summary>The first field of a form gets the keyboard as soon as the form appears.</summary>
    /// <summary>Inline inputs that appear on demand (new category, rename) take the focus.</summary>
    private void OnFocusWhenVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && e.NewValue is true)
        {
            box.Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void OnFocusOnLoad(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.Focus();
            box.SelectAll();
        }
    }
}
