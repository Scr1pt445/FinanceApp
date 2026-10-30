using FinanceApp.Application.Backups;
using FinanceApp.Application.Settings;
using FinanceApp.Infrastructure;
using FinanceApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Mobile;

/// <summary>
/// Gets the database ready once per process: migrations, the starter catalog on the first run
/// (base currency from the settings — RUB by default) and the daily copy (ADR 0012).
/// </summary>
public sealed class AppStartup(FinancePaths paths, IServiceScopeFactory scopes, ISettingsStore settings, IDatabaseBackup backups)
{
    private Task? _ready;

    /// <summary>
    /// A fresh install: no database yet, so the base currency has not been chosen. Installs that already have
    /// data skip the setup — they chose RUB when it was the only option.
    /// </summary>
    public bool NeedsSetup => !File.Exists(paths.DatabasePath);

    /// <summary>Safe to await from every screen; the work runs once, off the UI thread.</summary>
    public Task EnsureReadyAsync() => _ready ??= Task.Run(InitializeAsync);

    private async Task InitializeAsync()
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(settings.Load().BaseCurrency);
        }

        try
        {
            await backups.EnsureDailyAsync();
        }
#pragma warning disable CA1031 // A failed copy must not keep the app from opening; the next start tries again.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
