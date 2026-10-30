using CommunityToolkit.Mvvm.ComponentModel;
using FinanceApp.Application.Settings;
using FinanceApp.Presentation.Home;

namespace FinanceApp.Presentation.Settings;

/// <summary>
/// «Настройки» on the phone: how operations are recorded. A switch applies at once — the home screen shows or
/// hides the quick-entry line — and is saved.
/// </summary>
public sealed partial class PhoneSettingsViewModel(ISettingsStore settings, HomeViewModel home) : ObservableObject
{
    private bool _loading;

    /// <summary>The one-line entry on the home screen, for those who would rather type than tap.</summary>
    [ObservableProperty]
    public partial bool QuickEntryLine { get; set; }

    /// <summary>Why the last change was not saved; empty when it was.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    /// <summary>The app asks for a PIN when it opens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BiometricsMissing))]
    public partial bool LockOn { get; private set; }

    /// <summary>A fingerprint opens the app instead of the PIN (when the phone has one).</summary>
    [ObservableProperty]
    public partial bool Biometrics { get; set; }

    /// <summary>Set by the platform: the phone has a fingerprint (or face) enrolled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BiometricsMissing))]
    public partial bool BiometricsAvailable { get; set; }

    /// <summary>The lock is on but the phone has no fingerprint enrolled: say where to add one.</summary>
    public bool BiometricsMissing => LockOn && !BiometricsAvailable;

    public string BaseCurrencyText => $"Основная валюта — {settings.Load().BaseCurrency}. Её выбирают один раз, при первом запуске.";

    /// <summary>Reads the settings and applies them to the home screen; called before it is first shown.</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            QuickEntryLine = settings.Load().QuickEntryLine;
            home.QuickEntryOn = QuickEntryLine;
            LockOn = settings.Load().Lock.IsOn;
            Biometrics = settings.Load().Lock.Biometrics;
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnQuickEntryLineChanged(bool value)
    {
        home.QuickEntryOn = value;
        if (!_loading)
        {
            _ = SaveAsync(value);
        }
    }

    /// <summary>A PIN was just set (<see cref="PinSetupViewModel"/>): show the lock as on.</summary>
    public void Reload() => Load();

    /// <summary>Removes the PIN: the app opens without asking.</summary>
    public Task TurnOffLockAsync() => SaveAsync(s => s with { Lock = new LockSettings() }, () => LockOn = false);

    partial void OnBiometricsChanged(bool value)
    {
        if (!_loading && LockOn)
        {
            _ = SaveAsync(s => s with { Lock = s.Lock with { Biometrics = value } }, () => { });
        }
    }

    private async Task SaveAsync(Func<AppSettings, AppSettings> change, Action done)
    {
        try
        {
            await settings.SaveAsync(change(settings.Load()));
            done();
            Status = string.Empty;
        }
#pragma warning disable CA1031 // Said on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось сохранить: {ex.Message}";
        }
    }

    private async Task SaveAsync(bool quickEntryLine)
    {
        try
        {
            await settings.SaveAsync(settings.Load() with { QuickEntryLine = quickEntryLine });
            Status = string.Empty;
        }
#pragma warning disable CA1031 // The switch keeps working for this session; say that it was not remembered.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось сохранить: {ex.Message}";
        }
    }
}
