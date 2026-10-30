using FinanceApp.Application.Accounts;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Presentation.Setup;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Mobile;

/// <summary>
/// The first run's choices, in order: the base currency goes into the settings before the database is created,
/// so the starter accounts are made in it; then their balances and the first envelope.
/// </summary>
public sealed class SetupBackend(ISettingsStore settings, AppStartup startup, IServiceScopeFactory scopes) : ISetupBackend
{
    public async Task CompleteAsync(SetupChoices choices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choices);
        await settings.SaveAsync(settings.Load() with { BaseCurrency = choices.BaseCurrency, QuickEntryLine = choices.QuickEntryLine }, cancellationToken);
        await startup.EnsureReadyAsync();

        await using var scope = scopes.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var catalog = await scope.ServiceProvider.GetRequiredService<CatalogService>().LoadAsync(cancellationToken);
        foreach (var (name, amount) in choices.OpeningBalances)
        {
            if (catalog.Accounts.FirstOrDefault(a => a.Name == name) is { } account)
            {
                await accounts.UpdateAsync(account.Id, new AccountInput(account.Name, account.Kind, OpeningBalance: amount), cancellationToken);
            }
        }

        // Asking again after a failed attempt must not trip over the envelope made the first time.
        if (choices.Envelope is { } envelope && catalog.Accounts.All(a => !string.Equals(a.Name, envelope, StringComparison.CurrentCultureIgnoreCase)))
        {
            await accounts.CreateAsync(new AccountInput(envelope, AccountKind.Envelope, GoalAmount: choices.EnvelopeGoal), cancellationToken);
        }
    }
}
