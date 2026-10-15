using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Settings;

namespace FinanceApp.Presentation.Settings;

/// <summary>
/// «Горячая клавиша» window: the user presses a combination, sees whether it is valid, and saves it.
/// The new hotkey is registered first and persisted only if registration succeeded.
/// </summary>
public sealed partial class HotKeySettingsViewModel(IHotKeyRegistrar registrar, ISettingsStore settings) : ObservableObject
{
    public const string PressPrompt = "Нажмите новое сочетание клавиш";

    private HotKeyGesture? _candidate;

    /// <summary>The working hotkey, or a warning that there is none.</summary>
    [ObservableProperty]
    public partial string Current { get; set; } = string.Empty;

    /// <summary>What the user is pressing right now, e.g. «Ctrl+Alt+…» or «Ctrl+Shift+Q».</summary>
    [ObservableProperty]
    public partial string Recorded { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = PressPrompt;

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool HasCandidate { get; set; }

    /// <summary>Raised after the new hotkey is registered and saved; the window closes.</summary>
    public event EventHandler<HotKeyGesture>? Saved;

    /// <param name="startupError">Why the hotkey from the settings did not work at startup, if it did not.</param>
    public void Load(string? startupError = null)
    {
        Current = registrar.Current?.ToString() ?? "не назначена";
        SetCandidate(null, string.Empty);
        if (startupError is not null)
        {
            ShowProblem(startupError + " Выберите другое сочетание.");
        }
        else
        {
            ShowInfo(PressPrompt);
        }
    }

    /// <summary>Called by the view on every key press. <paramref name="key"/> is <c>null</c> while only modifiers are held.</summary>
    public void Record(HotKeyModifiers modifiers, string? key)
    {
        if (key is null)
        {
            var held = new HotKeyGesture(modifiers, "…").ToString();
            SetCandidate(null, modifiers == HotKeyModifiers.None ? string.Empty : held);
            ShowInfo(PressPrompt);
            return;
        }

        var gesture = new HotKeyGesture(modifiers, key);
        if (gesture.Problem is { } problem)
        {
            SetCandidate(null, gesture.ToString());
            ShowProblem(problem);
            return;
        }

        SetCandidate(gesture, gesture.ToString());
        ShowInfo(gesture.ToString() == Current ? "Это сочетание уже используется для быстрого ввода." : "Enter или «Сохранить» — применить.");
    }

    [RelayCommand]
    private void UseDefault()
    {
        HotKeyGesture.TryParse(AppSettings.DefaultQuickEntryHotKey, out var gesture);
        SetCandidate(gesture, gesture!.ToString());
        ShowInfo("Сочетание по умолчанию. «Сохранить» — применить.");
    }

    [RelayCommand(CanExecute = nameof(HasCandidate))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var gesture = _candidate!;
        if (!registrar.TryRegister(gesture, out var error))
        {
            ShowProblem(error ?? $"Не удалось назначить {gesture}.");
            return;
        }

        await settings.SaveAsync(settings.Load() with { QuickEntryHotKey = gesture.ToString() }, cancellationToken);
        Current = gesture.ToString();
        SetCandidate(null, string.Empty);
        ShowInfo($"Готово: быстрый ввод — {gesture}.");
        Saved?.Invoke(this, gesture);
    }

    private void SetCandidate(HotKeyGesture? gesture, string recorded)
    {
        _candidate = gesture;
        HasCandidate = gesture is not null;
        Recorded = recorded;
    }

    private void ShowInfo(string message)
    {
        Message = message;
        HasProblem = false;
    }

    private void ShowProblem(string message)
    {
        Message = message;
        HasProblem = true;
    }
}
