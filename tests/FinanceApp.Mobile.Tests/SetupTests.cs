using FinanceApp.Application.Accounts;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Setup;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Mobile.Tests;

/// <summary>A fresh install: the tour, the base currency and the balances come before the tabs.</summary>
public sealed class SetupTests : IAsyncLifetime
{
    private MobileApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await MobileApp.StartAsync(ready: false);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task FreshInstall_SetsUp_ThenShowsTheTabs()
    {
        _app.Get<AppStartup>().NeedsSetup.ShouldBeTrue();
        var window = (Window)((IApplication)_app.App).CreateWindow(null!);
        var setup = window.Page.ShouldBeOfType<SetupPage>().Setup;

        setup.SkipCommand.Execute(null);
        setup.ChooseCurrencyCommand.Execute(SetupViewModel.Currencies.Single(c => c.Code.Value == "BYN"));
        await setup.NextCommand.ExecuteAsync(null);
        setup.Balances.Single(b => b.Account == "Карта").AmountText = "1200";
        setup.EnvelopeName = "Отпуск";
        setup.EnvelopeGoalText = "5000";
        setup.QuickEntryLine = true;
        await setup.NextCommand.ExecuteAsync(null);

        setup.Problem.ShouldBeEmpty();
        await WaitUntil(() => window.Page is AppShell);
        _app.Get<AppStartup>().NeedsSetup.ShouldBeFalse();
        _app.Get<ISettingsStore>().Load().BaseCurrency.Value.ShouldBe("BYN");
        _app.Get<ISettingsStore>().Load().QuickEntryLine.ShouldBeTrue();
        _app.Get<Presentation.Home.HomeViewModel>().QuickEntryOn.ShouldBeTrue();

        await using var scope = _app.Get<IServiceScopeFactory>().CreateAsyncScope();
        var catalog = await scope.ServiceProvider.GetRequiredService<CatalogService>().LoadAsync(Ct);
        catalog.Accounts.Single(a => a.Name == "Карта").ShouldSatisfyAllConditions(
            a => a.Currency.Value.ShouldBe("BYN"),
            a => a.OpeningBalance.ShouldBe(1200m));
        catalog.Accounts.Single(a => a.Name == "Отпуск").ShouldSatisfyAllConditions(
            a => a.IsEnvelope.ShouldBeTrue(),
            a => a.GoalAmount.ShouldBe(5000m));
    }

    [Fact]
    public async Task RetryAfterAFailure_DoesNotDuplicateTheEnvelope()
    {
        var backend = _app.Get<ISetupBackend>();
        var choices = new SetupChoices(CurrencyCode.Rub, new Dictionary<string, decimal> { ["Наличные"] = 300m }, "Подушка", null);

        await backend.CompleteAsync(choices, Ct);
        await backend.CompleteAsync(choices, Ct);

        await using var scope = _app.Get<IServiceScopeFactory>().CreateAsyncScope();
        var catalog = await scope.ServiceProvider.GetRequiredService<CatalogService>().LoadAsync(Ct);
        catalog.Accounts.Count(a => a.Name == "Подушка").ShouldBe(1);
        catalog.Accounts.Single(a => a.Name == "Наличные").OpeningBalance.ShouldBe(300m);
    }

    [Fact]
    public async Task Tour_FromMore_ShowsEverySlide()
    {
        var tour = SetupViewModel.ForTour();
        var page = new SetupPage(tour);

        for (var i = 1; i < SetupViewModel.Slides.Count; i++)
        {
            await tour.NextCommand.ExecuteAsync(null);
            page.BindingContext.ShouldBeSameAs(tour);
        }

        tour.NextText.ShouldBe("Понятно");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(20, Ct);
        }

        condition().ShouldBeTrue();
    }
}
