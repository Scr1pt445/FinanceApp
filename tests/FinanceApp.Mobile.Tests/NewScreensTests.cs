using FinanceApp.Application.Recurring;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.Settings;
using Microsoft.Maui.Controls.Xaml.Diagnostics;

namespace FinanceApp.Mobile.Tests;

/// <summary>Recurring operations, limits, the lock and the shortcuts, built and bound as on the phone.</summary>
public sealed class NewScreensTests : IAsyncLifetime
{
    private readonly List<string> _bindingFailures = [];
    private MobileApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public async ValueTask InitializeAsync()
    {
        BindingDiagnostics.BindingFailed += OnBindingFailed;
        _app = await MobileApp.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        BindingDiagnostics.BindingFailed -= OnBindingFailed;
        await _app.DisposeAsync();
    }

    private async Task<RecurringOperation> RentAsync(DateOnly next)
    {
        var backend = _app.Get<IFinanceBackend>();
        var catalog = await backend.LoadCatalogAsync(Ct);
        return await backend.AddRecurringAsync(new RecurringInput(
            TransactionType.Expense, 30_000m, catalog.Categories.First(c => c.Type == TransactionType.Expense).Id,
            catalog.DefaultAccount(TransactionType.Expense)!.Id, null, "Аренда", RecurrenceFrequency.Monthly, next), Ct);
    }

    [Fact]
    public async Task Home_ShowsDueOperations_AndLimitWarnings()
    {
        await RentAsync(Today);
        var backend = _app.Get<IFinanceBackend>();
        var food = (await backend.LoadCatalogAsync(Ct)).Categories.Single(c => c.Name == "Еда" && c.Type == TransactionType.Expense);
        await backend.SetCategoryLimitAsync(food.Id, 1000m, Ct);
        var home = _app.Get<HomeViewModel>();
        await home.OpenAsync(Ct);
        home.QuickEntry.Input = "950 еда";
        await home.QuickEntry.SaveCommand.ExecuteAsync(null);
        await home.RefreshAsync(Ct);

        _app.Get<HomePage>();
        home.Due.ShouldHaveSingleItem().Title.ShouldBe("Аренда");
        home.LimitWarnings.ShouldHaveSingleItem().Title.ShouldBe("Еда");

        await home.Due[0].RecordCommand.ExecuteAsync(null);
        home.Due.ShouldBeEmpty();
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task RecurringPage_ListsAndEdits()
    {
        await RentAsync(Today.AddDays(10));
        var page = _app.Get<RecurringPage>();
        var list = (RecurringViewModel)page.BindingContext;

        await list.LoadAsync(Ct);
        list.EditCommand.Execute(list.Items.ShouldHaveSingleItem());

        list.IsEditing.ShouldBeTrue();
        list.Editor!.CancelCommand.Execute(null);
        list.IsEditing.ShouldBeFalse();
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Categories_And_Summary_ShowLimits()
    {
        var shell = _app.Get<ShellViewModel>();
        _app.Get<CategoriesPage>();
        await shell.Categories.LoadAsync(Ct);
        var food = shell.Categories.Expenses.First(c => c.Name == "Еда");
        food.LimitInput = "5000";
        await food.SaveLimitCommand.ExecuteAsync(null);

        _app.Get<SummaryPage>();
        await shell.Summary.LoadAsync(Ct);

        shell.Summary.Limits.ShouldHaveSingleItem().Title.ShouldBe("Еда");
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task LockPage_Unlocks_WithThePin()
    {
        var settings = _app.Get<ISettingsStore>();
        await settings.SaveAsync(settings.Load() with { Lock = PinCode.Create("1357", biometrics: false) }, Ct);
        var lockScreen = _app.Get<LockViewModel>();
        var unlocked = false;
        lockScreen.Unlocked += (_, _) => unlocked = true;
        var page = new LockPage(lockScreen, new NoBiometrics());
        var keys = Descendants<Button>(page).ToList();

        foreach (var digit in "1357")
        {
            ((IButtonController)keys.First(b => b.Text == digit.ToString())).SendClicked();
        }

        unlocked.ShouldBeTrue();
        new PinSetupPage(_app.Get<PinSetupViewModel>()).Setup.Title.ShouldBe("Придумайте PIN");
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public void MorePage_HasTheNewSettings()
    {
        var page = _app.Get<MorePage>();

        Descendants<Label>(page).Select(l => l.Text).ShouldContain("Регулярные операции");
        Descendants<Label>(page).Select(l => l.Text).ShouldContain("Вход по PIN");
        Descendants<Button>(page).Select(b => b.Text).ShouldContain("Восстановить из копии");
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public void Shortcuts_WaitForTheApp_AndTakeOnlyKnownKinds()
    {
        PendingAction.Request("bogus");
        PendingAction.Take().ShouldBeNull();

        PendingAction.Request(PendingAction.Income);
        PendingAction.Take().ShouldBe(PendingAction.Income);
        PendingAction.Take().ShouldBeNull();
    }

    private static IEnumerable<T> Descendants<T>(Element root) =>
        (root is T self ? [self] : Enumerable.Empty<T>()).Concat(((IElementController)root).LogicalChildren.SelectMany(Descendants<T>));

    private void OnBindingFailed(object? sender, BindingBaseErrorEventArgs e) => _bindingFailures.Add(e.Message);
}
