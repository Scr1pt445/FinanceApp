using System.ComponentModel;
using System.Windows.Input;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Desktop.Interop;

/// <summary>
/// Owns the quick-entry hotkey. A new gesture is registered before the old one is released,
/// so a failed change keeps the previous hotkey working.
/// </summary>
internal sealed class HotKeyService : IHotKeyRegistrar, IDisposable
{
    private const int ErrorHotKeyAlreadyRegistered = 1409;

    private GlobalHotKey? _registration;

    public event EventHandler? Pressed;

    /// <summary>Raised after <see cref="Current"/> changed (the tray menu shows it).</summary>
    public event EventHandler? Changed;

    public HotKeyGesture? Current { get; private set; }

    /// <summary>While true, presses are ignored — the hotkey settings window is recording a new one.</summary>
    public bool IsPaused { get; set; }

    public bool TryRegister(HotKeyGesture gesture, out string? error)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        error = null;
        if (gesture == Current)
        {
            return true;
        }

        if (!HotKeyKeys.TryToWpf(gesture, out var modifiers, out var key))
        {
            error = $"{gesture} нельзя назначить.";
            return false;
        }

        GlobalHotKey registration;
        try
        {
            registration = GlobalHotKey.Register(modifiers, key);
        }
        catch (Win32Exception ex)
        {
            error = ex.NativeErrorCode == ErrorHotKeyAlreadyRegistered
                ? $"{gesture} уже занято другим приложением."
                : $"Не удалось назначить {gesture}: {ex.Message}";
            return false;
        }

        registration.Pressed += OnPressed;
        _registration?.Dispose();
        _registration = registration;
        Current = gesture;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose() => _registration?.Dispose();

    private void OnPressed(object? sender, EventArgs e)
    {
        if (!IsPaused)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>Maps between WPF keys and the UI-independent <see cref="HotKeyGesture"/>.</summary>
internal static class HotKeyKeys
{
    public static bool TryToWpf(HotKeyGesture gesture, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        if (gesture.Modifiers.HasFlag(HotKeyModifiers.Ctrl))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (gesture.Modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (gesture.Modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (gesture.Modifiers.HasFlag(HotKeyModifiers.Win))
        {
            modifiers |= ModifierKeys.Windows;
        }

        var name = gesture.Key is { Length: 1 } k && char.IsAsciiDigit(k[0]) ? "D" + k : gesture.Key;
        return Enum.TryParse(name, ignoreCase: false, out key) && HotKeyGesture.IsSupportedKey(gesture.Key);
    }

    public static HotKeyModifiers FromWpf(ModifierKeys modifiers, bool windowsKeyDown)
    {
        var result = HotKeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HotKeyModifiers.Ctrl;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HotKeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HotKeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows) || windowsKeyDown)
        {
            result |= HotKeyModifiers.Win;
        }

        return result;
    }

    /// <summary>The gesture key name, or <c>null</c> for a modifier key (still being held).</summary>
    public static string? KeyName(Key key) =>
        key switch
        {
            Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin or Key.System or Key.None => null,
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => key.ToString(),
        };
}
