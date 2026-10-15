using System.Windows;
using System.Windows.Interop;
using FinanceApp.Presentation.QuickEntry;
using WinForms = System.Windows.Forms;

namespace FinanceApp.Desktop;

/// <summary>
/// Created once at startup and only hidden/shown afterwards, so it appears instantly (requirements, section 6).
/// Hides on Enter (after saving), on Esc and when it loses focus.
/// </summary>
internal sealed partial class QuickEntryWindow : Window
{
    private readonly QuickEntryViewModel _viewModel;

    public QuickEntryWindow(QuickEntryViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        viewModel.Saved += (_, _) => Hide();
        viewModel.CloseRequested += (_, _) => Hide();
        Deactivated += (_, _) =>
        {
            if (!viewModel.IsBusy)
            {
                Hide();
            }
        };

        // Create the native window now; the first show then costs no layout of a new HWND.
        new WindowInteropHelper(this).EnsureHandle();
    }

    public void ShowAndFocus()
    {
        PositionOnActiveScreen();
        Show();
        Activate();
        InputBox.Focus();
        _ = _viewModel.OpenAsync();
    }

    /// <summary>Horizontally centered, at a third of the height of the screen with the mouse cursor.</summary>
    private void PositionOnActiveScreen()
    {
        var area = WinForms.Screen.FromPoint(WinForms.Cursor.Position).WorkingArea;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var left = area.Left / dpi.DpiScaleX;
        var top = area.Top / dpi.DpiScaleY;
        var width = area.Width / dpi.DpiScaleX;
        var height = area.Height / dpi.DpiScaleY;

        Left = left + ((width - Width) / 2);
        Top = top + (height / 3) - 60;
    }
}
