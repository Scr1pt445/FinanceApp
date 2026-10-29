using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.Hosting;
using FinanceApp.Presentation.QuickEntry;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Infrastructure.Tests.Hosting;

/// <summary>The phone's home screen on the real database: every call of the backends gets its own scope.</summary>
public sealed class ScopedBackendsTests : IAsyncLifetime
{
    private TestApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await TestApp.CreateAsync(exportEnabled: false);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task QuickEntry_SavesThroughOneScope_HomeSeesItThroughAnother()
    {
        var scopes = _app.Get<IServiceScopeFactory>();
        var home = new HomeViewModel(new ScopedFinanceBackend(scopes), new QuickEntryViewModel(new ScopedQuickEntryBackend(scopes)), TimeProvider.System);
        await home.OpenAsync(Ct);
        home.TotalText.ShouldBe("0 ₽");
        home.IsEmpty.ShouldBeTrue();

        home.QuickEntry.Input = "350 еда кофе";
        home.QuickEntry.SaveCommand.CanExecute(null).ShouldBeTrue();
        await home.QuickEntry.SaveCommand.ExecuteAsync(null);
        await home.RefreshAsync(Ct); // the refresh after a save runs in the background

        home.LastSaved.ShouldBe("Сохранено: Расход 350 ₽");
        home.TotalText.ShouldBe("−350 ₽");
        var row = home.Recent.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        (row.Title, row.AmountText).ShouldBe(("Еда", "−350 ₽"));
        home.Warning.ShouldBeEmpty();
    }
}
