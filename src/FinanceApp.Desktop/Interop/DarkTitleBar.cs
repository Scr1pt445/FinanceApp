using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FinanceApp.Desktop.Interop;

/// <summary>Asks Windows 10 (20H1+) / 11 to draw the standard title bar dark, to match the dark theme.</summary>
internal static partial class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var enabled = 1;

        // Older Windows versions ignore the attribute and keep the light title bar — harmless.
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
