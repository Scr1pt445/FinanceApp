using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace FinanceApp.Desktop.Interop;

/// <summary>
/// A system-wide hotkey via <c>RegisterHotKey</c>, delivered to a hidden message-only window.
/// Windows lets the process that receives <c>WM_HOTKEY</c> bring its window to the foreground.
/// </summary>
internal sealed partial class GlobalHotKey : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int HotKeyId = 0x4D46; // "MF"
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly HwndSource _source;
    private bool _registered;

    private GlobalHotKey(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
        _source = new HwndSource(new HwndSourceParameters("FinanceApp.HotKey") { ParentWindow = MessageOnlyParent, Width = 0, Height = 0 });
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public ModifierKeys Modifiers { get; }

    public Key Key { get; }

    public string DisplayName => string.Join("+", new[]
    {
        Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl" : null,
        Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt" : null,
        Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift" : null,
        Modifiers.HasFlag(ModifierKeys.Windows) ? "Win" : null,
        Key.ToString(),
    }.Where(part => part is not null));

    /// <exception cref="Win32Exception">The combination is taken by another application.</exception>
    public static GlobalHotKey Register(ModifierKeys modifiers, Key key)
    {
        var hotKey = new GlobalHotKey(modifiers, key);
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(hotKey._source.Handle, HotKeyId, (uint)modifiers | ModNoRepeat, virtualKey))
        {
            var error = new Win32Exception(Marshal.GetLastPInvokeError());
            hotKey.Dispose();
            throw error;
        }

        hotKey._registered = true;
        return hotKey;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotKeyId);
            _registered = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}
