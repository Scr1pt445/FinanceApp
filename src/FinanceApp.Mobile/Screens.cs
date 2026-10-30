using FinanceApp.Presentation.Data;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Mobile;

/// <summary>
/// The screens keep their own copies of the data; after a change made elsewhere — a rate, a limit, a recurring
/// operation recorded, a copy restored — they all load again, and the home-screen widget with them.
/// </summary>
public sealed class Screens
{
    private readonly HomeViewModel home;
    private readonly ShellViewModel shell;
    private readonly PhoneSettingsViewModel settings;

    /// <summary>Listens to every view model that changes data the others show.</summary>
    public Screens(HomeViewModel home, ShellViewModel shell, PhoneSettingsViewModel settings, DataViewModel data, RecurringViewModel recurring, CurrenciesViewModel currencies)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recurring);
        ArgumentNullException.ThrowIfNull(currencies);
        this.home = home;
        this.shell = shell;
        this.settings = settings;
        data.Restored += async (_, _) => await RefreshAllAsync();
        recurring.Changed += async (_, _) => await RefreshAllAsync();
        currencies.Changed += async (_, _) => await RefreshAllAsync();
        shell.Categories.LimitsChanged += async (_, _) => await RefreshAllAsync();
        home.Changed += async (_, _) => await shell.RefreshAsync();
    }

    public async Task RefreshAllAsync()
    {
        try
        {
            settings.Load();
            await home.RefreshAsync();
            await shell.RefreshAsync();
        }
#pragma warning disable CA1031 // Called from event handlers; each screen shows its own load errors anyway.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }
    }
}
