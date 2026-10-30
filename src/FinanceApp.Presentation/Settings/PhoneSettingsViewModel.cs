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

    public string BaseCurrencyText => $"Основная валюта — {settings.Load().BaseCurrency}. Её выбирают один раз, при первом запуске.";

    /// <summary>Reads the settings and applies them to the home screen; called before it is first shown.</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            QuickEntryLine = settings.Load().QuickEntryLine;
            home.QuickEntryOn = QuickEntryLine;
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
