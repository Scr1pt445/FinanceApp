using FinanceApp.Application.Accounts;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests.Settings;

/// <summary>The user's currencies, saved in settings.json and read back by the catalog and the totals.</summary>
public sealed class CurrencySettingsTests : IAsyncLifetime
{
    private TestApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await TestApp.CreateAsync(exportEnabled: false);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Saved_List_ReachesTheCatalog_AndTheTotalsLeaveARemovedCurrency()
    {
        var store = _app.Get<ISettingsStore>();
        await store.SaveAsync(store.Load() with { TotalsCurrency = CurrencyCode.Eur }, Ct);

        await using (var scope = _app.Scope())
        {
            await scope.Get<CurrencySettingsService>().SaveAsync([CurrencyCode.Byn, CurrencyCode.Parse("KZT"), CurrencyCode.Byn], Ct);
        }

        store.Load().Currencies.ShouldBe([CurrencyCode.Byn, CurrencyCode.Parse("KZT")]);
        store.Load().TotalsCurrency.ShouldBeNull();

        await using (var scope = _app.Scope())
        {
            var catalog = await scope.Get<CatalogService>().LoadAsync(Ct);
            catalog.Currencies.ShouldBe([CurrencyCode.Rub, CurrencyCode.Byn, CurrencyCode.Parse("KZT")]);
            var balances = await scope.Get<AccountBalancesService>().LoadAsync(cancellationToken: Ct);
            balances.DisplayChoices.ShouldBe(catalog.Currencies);
        }
    }
}
