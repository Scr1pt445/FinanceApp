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
/// (then a minute, and so on up to half an hour) — four digits are quickly guessed otherwise. The count is kept in the
/// settings, so closing the app does not give five new tries.
/// </summary>
public sealed partial class LockViewModel(ISettingsStore settings, TimeProvider timeProvider) : PinPadViewModel
{
    public const int FreeAttempts = 5;

    /// <summary>The longest wait: 30 s doubled six times, about half an hour.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(30 * 64);

    private readonly SemaphoreSlim _saving = new(1, 1);
    private int _failures;
    private DateTimeOffset? _retryAfter;
    private string? _countedFor;
    private bool _counted;

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
        Count();
        if (_retryAfter is not { } retryAfter)
        {
            return true;
        }

        var now = timeProvider.GetUtcNow();
        var left = retryAfter - now;

        // A wait longer than any the keypad sets means the clock was turned back: count it from now.
        if (left > LongestWait)
        {
            _retryAfter = now + LongestWait;
            left = LongestWait;
            Persist();
        }

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

        Count();
        _failures++;
        IsWrong = true;
        if (_failures >= FreeAttempts)
        {
            var wait = TimeSpan.FromSeconds(30 * (1 << Math.Min(_failures - FreeAttempts, 6)));
            _retryAfter = timeProvider.GetUtcNow() + wait;
            Message = $"Неверный PIN. Следующая попытка через {wait.TotalSeconds} с.";
        }
        else
        {
            Message = $"Неверный PIN. Осталось попыток: {FreeAttempts - _failures}.";
        }

        Persist();
    }

    private void Accept()
    {
        Count();
        if (_failures != 0 || _retryAfter is not null)
        {
            _failures = 0;
            _retryAfter = null;
            Persist();
        }

        Reset();
        Unlocked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes the count from the settings once, and again after a new PIN is set.</summary>
    private void Count()
    {
        var stored = settings.Load().Lock;
        if (_counted && _countedFor == stored.PinHash)
        {
            return;
        }

        (_failures, _retryAfter, _countedFor, _counted) = (stored.FailedAttempts, stored.RetryAfter, stored.PinHash, true);
    }

    /// <summary>Writes the count at once, one write at a time, each with the latest values.</summary>
    private void Persist() => _ = PersistAsync();

    private async Task PersistAsync()
    {
        await _saving.WaitAsync();
        try
        {
            var current = settings.Load();
            if (current.Lock.PinHash == _countedFor)
            {
                await settings.SaveAsync(current with { Lock = current.Lock with { FailedAttempts = _failures, RetryAfter = _retryAfter } });
            }
        }
#pragma warning disable CA1031 // The count still holds in memory until the app closes; nothing else to do.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        finally
        {
            _saving.Release();
        }
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
