using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Settings;

namespace FinanceApp.Presentation.Settings;

/// <summary>The four dots over a PIN keypad: filled for the digits typed.</summary>
public abstract partial class PinPadViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dots))]
    public partial string Entered { get; protected set; } = string.Empty;

    /// <summary>A hint or why the PIN was not taken.</summary>
    [ObservableProperty]
    public partial string Message { get; protected set; } = string.Empty;

    /// <summary>Set for a moment when the PIN was wrong: the dots shake.</summary>
    [ObservableProperty]
    public partial bool IsWrong { get; protected set; }

    public IReadOnlyList<bool> Dots => Enumerable.Range(0, PinCode.Length).Select(i => i < Entered.Length).ToList();

    [RelayCommand]
    private void Press(string digit)
    {
        if (Entered.Length >= PinCode.Length || digit is not { Length: 1 } || !char.IsAsciiDigit(digit[0]) || !CanType())
        {
            return;
        }

        IsWrong = false;
        Entered += digit;
        if (Entered.Length == PinCode.Length)
        {
            var pin = Entered;
            Entered = string.Empty;
            OnComplete(pin);
        }
    }

    [RelayCommand]
    private void Erase()
    {
        if (Entered.Length > 0)
        {
            Entered = Entered[..^1];
        }
    }

    protected virtual bool CanType() => true;

    protected abstract void OnComplete(string pin);
}

/// <summary>
/// The lock screen: the app opens after the PIN or a fingerprint. After five wrong PINs the keypad waits 30 seconds
/// (then a minute, and so on) — four digits are quickly guessed otherwise.
/// </summary>
public sealed partial class LockViewModel(ISettingsStore settings, TimeProvider timeProvider) : PinPadViewModel
{
    public const int FreeAttempts = 5;

    private int _failures;
    private DateTimeOffset _waitUntil;

    /// <summary>The app is locked: a PIN is set.</summary>
    public bool IsOn => settings.Load().Lock.IsOn;

    /// <summary>A fingerprint may open the app; the platform asks for it when the lock screen shows.</summary>
    public bool BiometricsOn => IsOn && settings.Load().Lock.Biometrics;

    /// <summary>Raised once the PIN or the fingerprint is accepted.</summary>
    public event EventHandler? Unlocked;

    /// <summary>The platform confirmed the fingerprint.</summary>
    public void UnlockByBiometrics() => Accept();

    public void Reset()
    {
        Entered = string.Empty;
        Message = BiometricsOn ? "Введите PIN или приложите палец" : "Введите PIN";
        IsWrong = false;
    }

    protected override bool CanType()
    {
        var left = _waitUntil - timeProvider.GetUtcNow();
        if (left <= TimeSpan.Zero)
        {
            return true;
        }

        Message = $"Слишком много попыток. Подождите {Math.Ceiling(left.TotalSeconds)} с.";
        return false;
    }

    protected override void OnComplete(string pin)
    {
        if (PinCode.Verify(settings.Load().Lock, pin))
        {
            Accept();
            return;
        }

        _failures++;
        IsWrong = true;
        if (_failures >= FreeAttempts)
        {
            var wait = TimeSpan.FromSeconds(30 * (1 << Math.Min(_failures - FreeAttempts, 6)));
            _waitUntil = timeProvider.GetUtcNow() + wait;
            Message = $"Неверный PIN. Следующая попытка через {wait.TotalSeconds} с.";
        }
        else
        {
            Message = $"Неверный PIN. Осталось попыток: {FreeAttempts - _failures}.";
        }
    }

    private void Accept()
    {
        _failures = 0;
        _waitUntil = default;
        Reset();
        Unlocked?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Setting a PIN: typed twice; saved only when both match.</summary>
public sealed partial class PinSetupViewModel(ISettingsStore settings) : PinPadViewModel
{
    private string? _first;

    public string Title => _first is null ? "Придумайте PIN" : "Повторите PIN";

    /// <summary>The PIN was saved (<c>true</c>) or the setup was closed (<c>false</c>).</summary>
    public event EventHandler<bool>? Finished;

    /// <summary>Whether a fingerprint may open the app too; asked on the settings screen.</summary>
    public bool Biometrics { get; set; }

    [RelayCommand]
    private void Cancel() => Finished?.Invoke(this, false);

    protected override void OnComplete(string pin)
    {
        if (_first is null)
        {
            _first = pin;
            Message = "Ещё раз, чтобы не ошибиться";
            OnPropertyChanged(nameof(Title));
            return;
        }

        if (_first != pin)
        {
            _first = null;
            IsWrong = true;
            Message = "PIN не совпал — придумайте заново";
            OnPropertyChanged(nameof(Title));
            return;
        }

        _ = SaveAsync(pin);
    }

    private async Task SaveAsync(string pin)
    {
        try
        {
            await settings.SaveAsync(settings.Load() with { Lock = PinCode.Create(pin, Biometrics) });
            Finished?.Invoke(this, true);
        }
#pragma warning disable CA1031 // Shown on the keypad; nothing is locked.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _first = null;
            Message = $"Не удалось сохранить: {ex.Message}";
            OnPropertyChanged(nameof(Title));
        }
    }
}
