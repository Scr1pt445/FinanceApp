using FinanceApp.Application.QuickEntry;
using FinanceApp.Domain;
using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Data;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;
using Microsoft.Maui.Controls.Xaml.Diagnostics;

namespace FinanceApp.Mobile.Tests;

/// <summary>
/// Every screen is built the way the phone builds it: from DI, under the app's theme, on a real database. A
/// missing style, a converter that throws or a broken data template fails here instead of crashing the app on
/// start — which is how the first APK failed.
/// </summary>
public sealed class PagesTests : IAsyncLifetime
{
    private readonly List<string> _bindingFailures = [];
    private MobileApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    [Fact]
    public void PageWithoutTheTheme_Fails_SoTheseTestsWouldCatchIt()
    {
        Microsoft.Maui.Controls.Application.Current = null;

        Should.Throw<Exception>(() => new AccountsPage(_app.Get<ShellViewModel>(), _app.Get<Func<RatesPage>>()));
    }

    [Fact]
    public void Shell_BuildsAllFiveTabs()
    {
        var shell = _app.Get<AppShell>();

        var tabs = shell.Items.ShouldHaveSingleItem().Items.Select(t => t.Title).ToList();
        tabs.ShouldBe(["Главная", "Операции", "Счета", "Итоги", "Ещё"]);
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public void Shell_SwitchesTabsByRoute()
    {
        var shell = _app.Get<AppShell>();

        shell.Show(AppShell.OperationsRoute);

        shell.Items[0].CurrentItem.ShouldBeSameAs(shell[AppShell.OperationsRoute]);
        shell.Items[0].CurrentItem.CurrentItem.Content.ShouldBeOfType<OperationsPage>();
    }

    [Fact]
    public async Task Screens_ShowRealData_AfterQuickEntry()
    {
        _app.Get<AppShell>();
        var home = _app.Get<HomeViewModel>();
        var shell = _app.Get<ShellViewModel>();

        await home.OpenAsync(Ct);
        home.QuickEntry.Input = "350 еда кофе";
        await home.QuickEntry.SaveCommand.ExecuteAsync(null);
        home.QuickEntry.Input = "+50000 зарплата";
        await home.QuickEntry.SaveCommand.ExecuteAsync(null);
        await home.RefreshAsync(Ct);
        await shell.RefreshAsync(Ct);
        await shell.Summary.LoadAsync(Ct);
        _app.Get<DataViewModel>().Load();

        home.Recent.ShouldHaveSingleItem().Rows.Count.ShouldBe(2);
        shell.Operations.Days.ShouldHaveSingleItem();
        shell.Accounts.ShouldNotBeEmpty();
        shell.Summary.IsEmpty.ShouldBeFalse();
        shell.Summary.Status.ShouldBeEmpty();
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task OperationForms_OpenForEveryKind()
    {
        var shell = _app.Get<ShellViewModel>();

        foreach (var command in new[] { shell.NewExpenseCommand, shell.NewIncomeCommand, shell.NewTransferCommand })
        {
            await command.ExecuteAsync(null);
            var page = new TransactionFormPage(shell.Form.ShouldNotBeNull());
            (page.Form.Tiles.Count > 0).ShouldBe(!page.Form.IsTransfer);
            page.Form.CancelCommand.Execute(null);
            shell.Form.ShouldBeNull();
        }

        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task EditingAnOperation_OpensItsForm()
    {
        var shell = _app.Get<ShellViewModel>();
        await using (var scope = _app.Get<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>().CreateAsyncScope())
        {
            await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<QuickEntryService>(scope.ServiceProvider).SaveAsync("350 еда кофе", Ct);
        }

        await shell.Operations.LoadAsync(Ct);
        shell.Operations.EditCommand.Execute(shell.Operations.Days[0].Rows[0]);
        await WaitUntil(() => shell.Form is not null);

        var page = new TransactionFormPage(shell.Form!);
        page.Form.IsNew.ShouldBeFalse();
        page.Form.AmountText.ShouldBe("350");
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task AccountForms_OpenForAccountsAndEnvelopes()
    {
        var shell = _app.Get<ShellViewModel>();

        await shell.NewEnvelopeCommand.ExecuteAsync(null);
        var envelope = new AccountFormPage(shell.AccountForm.ShouldNotBeNull());
        envelope.Form.IsEnvelope.ShouldBeTrue();
        envelope.Form.AddGoalCommand.Execute(null);

        await shell.NewAccountCommand.ExecuteAsync(null);
        new AccountFormPage(shell.AccountForm.ShouldNotBeNull()).Form.IsEnvelope.ShouldBeFalse();
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Categories_ListBothKinds()
    {
        var page = _app.Get<CategoriesPage>();
        var categories = _app.Get<ShellViewModel>().Categories;

        await categories.LoadAsync(Ct);

        page.BindingContext.ShouldBeSameAs(categories);
        categories.Expenses.ShouldNotBeEmpty();
        categories.Incomes.ShouldNotBeEmpty();
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task CurrencyChips_SwitchTheTotal_AndTheChoiceSurvivesARefresh()
    {
        var shell = _app.Get<ShellViewModel>();
        var page = _app.Get<AccountsPage>();
        await shell.RefreshAsync(Ct);
        Button Chip(CurrencyCode code) => Descendants<Button>(page).Single(b => b.BindingContext is CurrencyCode c && c == code);

        ((IButtonController)Chip(CurrencyCode.Usd)).SendClicked();
        await WaitUntil(() => shell.TotalText.EndsWith('$'));
        await shell.RefreshAsync(Ct);

        shell.TotalsCurrency.ShouldBe(CurrencyCode.Usd);
        Chip(CurrencyCode.Usd).BackgroundColor.ShouldBe(Palette.Accent);
        Chip(CurrencyCode.Rub).BackgroundColor.ShouldBe(Palette.Control);
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExpenseForm_TakesAReceiptInEuros()
    {
        var shell = _app.Get<ShellViewModel>();
        await shell.NewExpenseCommand.ExecuteAsync(null);
        var page = new TransactionFormPage(shell.Form!);
        var euro = Descendants<Button>(page).Single(b => b.BindingContext is CurrencyCode c && c == CurrencyCode.Eur);

        page.Form.AmountText = "20";
        ((IButtonController)euro).SendClicked();
        await WaitUntil(() => page.Form.ConversionText.Length > 0);

        page.Form.IsForeign.ShouldBeTrue();
        euro.BackgroundColor.ShouldBe(Palette.Accent);
        page.Form.RateMissing.ShouldBeTrue(); // the test rates know only USD
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExpenseForm_TakesItsOwnRate_AndCanRememberIt()
    {
        var shell = _app.Get<ShellViewModel>();
        await shell.NewExpenseCommand.ExecuteAsync(null);
        var page = new TransactionFormPage(shell.Form!);
        Button Find(string text) => Descendants<Button>(page).First(b => b.Text == text);
        page.Form.AmountText = "20";
        ((IButtonController)Descendants<Button>(page).Single(b => b.BindingContext is CurrencyCode c && c == CurrencyCode.Eur)).SendClicked();

        ((IButtonController)Find("Свой курс")).SendClicked();
        page.Form.ForeignRateText = "100";
        ((IButtonController)Find("На неделю")).SendClicked();

        page.Form.ForeignByRate.ShouldBeTrue();
        page.Form.ChargedPreview.ShouldBe("Спишется 2\u00a0000 ₽");
        page.Form.Remember.Period.ShouldBe(FinanceApp.Application.ExchangeRates.ManualRatePeriod.Week);
        Find("На неделю").BackgroundColor.ShouldBe(Palette.Accent);
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task RatesPage_AddsARate_AndTheTotalsUseIt()
    {
        var shell = _app.Get<ShellViewModel>();
        await shell.NewAccountCommand.ExecuteAsync(null);
        shell.AccountForm!.Name = "Наличные EUR";
        shell.AccountForm.CurrencyText = "EUR";
        shell.AccountForm.OpeningBalanceText = "10";
        await shell.AccountForm.SaveCommand.ExecuteAsync(null);
        await shell.RefreshAsync(Ct);
        shell.Warning.ShouldNotBeEmpty(); // the test rates know only USD

        var page = _app.Get<RatesPage>();
        var rates = (ManualRatesViewModel)page.BindingContext;
        await rates.LoadAsync(Ct);
        ((IButtonController)Descendants<Button>(page).First(b => b.BindingContext is CurrencyCode c && c == CurrencyCode.Eur)).SendClicked();
        rates.RateText = "100";
        await rates.AddCommand.ExecuteAsync(null);
        await shell.RefreshAsync(Ct);

        rates.Rates.ShouldHaveSingleItem().Title.ShouldBe("1 EUR = 100 RUB");
        shell.Warning.ShouldBeEmpty();
        shell.TotalText.ShouldBe("1\u00a0000 ₽");
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task QuickEntrySwitch_InMore_ShowsTheLineOnHome()
    {
        _app.Get<AppShell>();
        var home = _app.Get<HomeViewModel>();
        home.QuickEntryOn.ShouldBeFalse();
        var line = Descendants<Entry>(_app.Get<HomePage>()).First(e => e.Placeholder == "350 еда кофе");
        var settingsSwitch = Descendants<Switch>(_app.Get<MorePage>()).First(s => s.BindingContext is Presentation.Settings.PhoneSettingsViewModel);

        settingsSwitch.IsToggled = true;

        home.QuickEntryOn.ShouldBeTrue();
        line.Parent.ShouldNotBeNull();
        await WaitUntil(() => _app.Get<Application.Settings.ISettingsStore>().Load().QuickEntryLine);
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public void PeriodChips_ChangeThePeriod()
    {
        var page = _app.Get<SummaryPage>();
        var summary = _app.Get<ShellViewModel>().Summary;
        var year = Descendants<Button>(page).Single(b => b.BindingContext is PeriodChoice { Kind: PeriodKind.Year });

        ((IButtonController)year).SendClicked();

        summary.Choice.Kind.ShouldBe(PeriodKind.Year);
        year.BackgroundColor.ShouldBe(Palette.Accent);
    }

    [Fact]
    public void StartupPage_ShowsAnError() =>
        Should.NotThrow(() => new StartupPage().ShowError("Не удалось открыть базу данных: диск полон"));

    [Theory]
    [InlineData("Accent")]
    [InlineData("Window")]
    [InlineData("Surface")]
    [InlineData("Control")]
    [InlineData("Text")]
    [InlineData("MutedText")]
    [InlineData("Warning")]
    [InlineData("Income")]
    [InlineData("Saving")]
    [InlineData("AccentLight")]
    [InlineData("AccentDeep")]
    public void Theme_MatchesPalette(string key)
    {
        _app.App.Resources.TryGetValue(key, out var value).ShouldBeTrue();
        var expected = typeof(Palette).GetField(key)!.GetValue(null);

        value.ShouldBe(expected);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20, Ct);
        }

        condition().ShouldBeTrue();
    }

    private static IEnumerable<T> Descendants<T>(Element root) =>
        (root is T self ? [self] : Enumerable.Empty<T>()).Concat(((IElementController)root).LogicalChildren.SelectMany(Descendants<T>));

    private void OnBindingFailed(object? sender, BindingBaseErrorEventArgs e) => _bindingFailures.Add(e.Message);
}
