using System.Diagnostics.CodeAnalysis;

namespace FinanceApp.Presentation.Settings;

[Flags]
public enum HotKeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global hotkey such as «Ctrl+Alt+M», independent of WPF so it can be validated and tested anywhere.
/// <see cref="Key"/> is a letter (A–Z), a digit (0–9), a function key (F1–F24) or «Space».
/// </summary>
public sealed record HotKeyGesture(HotKeyModifiers Modifiers, string Key)
{
    private static readonly (HotKeyModifiers Flag, string Name)[] ModifierNames =
        [(HotKeyModifiers.Ctrl, "Ctrl"), (HotKeyModifiers.Alt, "Alt"), (HotKeyModifiers.Shift, "Shift"), (HotKeyModifiers.Win, "Win")];

    public static bool IsSupportedKey(string? key) =>
        key is { Length: 1 } && (char.IsAsciiLetterUpper(key[0]) || char.IsAsciiDigit(key[0]))
        || key == "Space"
        || key is { Length: >= 2 and <= 3 } && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 24;

    /// <summary>Why the gesture cannot be a global hotkey, or <c>null</c> if it can.</summary>
    public string? Problem =>
        !IsSupportedKey(Key) ? "Используйте букву, цифру, F1–F24 или пробел."
        : (Modifiers & (HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Win)) == 0
            ? "Нужен Ctrl, Alt или Win — иначе клавиша будет мешать обычному набору текста."
            : null;

    public static bool TryParse(string? text, [NotNullWhen(true)] out HotKeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotKeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => (HotKeyModifiers?)HotKeyModifiers.Ctrl,
                "ALT" => HotKeyModifiers.Alt,
                "SHIFT" => HotKeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotKeyModifiers.Win,
                _ => null,
            };

            if (part is { } modifier)
            {
                modifiers |= modifier;
            }
            else if (key is null)
            {
                key = raw.Equals("space", StringComparison.OrdinalIgnoreCase) ? "Space" : raw.ToUpperInvariant();
            }
            else
            {
                return false;
            }
        }

        if (key is null)
        {
            return false;
        }

        var candidate = new HotKeyGesture(modifiers, key);
        if (candidate.Problem is not null)
        {
            return false;
        }

        gesture = candidate;
        return true;
    }

    /// <summary>«Ctrl+Alt+M» — modifiers always in the same order.</summary>
    public override string ToString() =>
        string.Join('+', ModifierNames.Where(m => Modifiers.HasFlag(m.Flag)).Select(m => m.Name).Append(Key));
}
