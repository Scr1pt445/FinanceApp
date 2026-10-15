using System.Windows;
using System.Windows.Input;
using FinanceApp.Desktop.Interop;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Desktop;

/// <summary>Records the combination the user presses. Enter saves, Esc closes; every other key is recorded.</summary>
internal sealed partial class HotKeyWindow : Window
{
    private readonly HotKeySettingsViewModel _viewModel;

    public HotKeyWindow(HotKeySettingsViewModel viewModel, HotKeyService hotKeys, string? startupError)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        DarkTitleBar.Apply(this);
        viewModel.Load(startupError);
        viewModel.Saved += (_, _) => Close();

        // The current hotkey would otherwise pop up the quick-entry window while recording.
        hotKeys.IsPaused = true;
        Closed += (_, _) => hotKeys.IsPaused = false;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (modifiers == ModifierKeys.None && key == Key.Escape)
        {
            Close();
            return;
        }

        if (modifiers == ModifierKeys.None && key == Key.Enter)
        {
            if (_viewModel.SaveCommand.CanExecute(null))
            {
                _viewModel.SaveCommand.Execute(null);
            }

            return;
        }

        var windowsDown = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        _viewModel.Record(HotKeyKeys.FromWpf(modifiers, windowsDown), HotKeyKeys.KeyName(key));
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
