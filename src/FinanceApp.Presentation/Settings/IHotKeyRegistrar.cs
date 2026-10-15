namespace FinanceApp.Presentation.Settings;

/// <summary>Registers the quick-entry hotkey with the operating system (the desktop app implements it).</summary>
public interface IHotKeyRegistrar
{
    /// <summary>The gesture that currently works, or <c>null</c> if none could be registered.</summary>
    HotKeyGesture? Current { get; }

    /// <summary>
    /// Registers <paramref name="gesture"/>; the previous hotkey is released only on success,
    /// so a failed attempt never leaves the user without a working one.
    /// </summary>
    /// <param name="error">User-facing reason, e.g. the combination is taken by another app.</param>
    bool TryRegister(HotKeyGesture gesture, out string? error);
}
