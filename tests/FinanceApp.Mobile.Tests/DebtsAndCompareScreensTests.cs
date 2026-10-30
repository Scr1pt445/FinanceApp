using FinanceApp.Domain;
using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.QuickEntry;
using Microsoft.Maui.Controls.Xaml.Diagnostics;

namespace FinanceApp.Mobile.Tests;

/// <summary>«Долги» and «Сравнение месяцев», built and bound as on the phone.</summary>
public sealed class DebtsAndCompareScreensTests : IAsyncLifetime
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

    [Fact]
    public async Task DebtsPage_AddsADebt_AndTheMenuShowsIt()
    {
        var page = _app.Get<DebtsPage>();
        var debts = (DebtsViewModel)page.BindingContext;
        await debts.LoadAsync(Ct);

        debts.NewCommand.Execute(null);
        var editor = debts.Editor.ShouldNotBeNull();
        editor.Currencies.ShouldContain(CurrencyCode.Rub);
        editor.ChooseDirectionCommand.Execute(DebtDirectionChoice.All[1]);
        editor.PersonLabel.ShouldBe("Кому должен");
        editor.Person = "Банк";
        editor.AmountText = "1500";
        editor.DueText = Format.DateText(Today.AddDays(-1));
        editor.DateText = Format.DateText(Today.AddDays(-10));
        editor.SaveCommand.Execute(null);
        for (var i = 0; i < 200 && debts.IsEditing; i++)
        {
            await Task.Delay(10, Ct);
        }

        debts.IsEditing.ShouldBeFalse();
        debts.Open.ShouldHaveSingleItem().IsOverdue.ShouldBeTrue();
        debts.HasOverdue.ShouldBeTrue();

        _app.Get<MorePage>();
        debts.Summary.ShouldEndWith("1 просрочен");
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task ComparePage_ShowsTheMonths_AndTheForecast()
    {
        var quick = _app.Get<QuickEntryViewModel>();
        quick.Input = "950 еда";
        await quick.SaveCommand.ExecuteAsync(null);

        _app.Get<SummaryPage>();
        var page = _app.Get<CompareMonthsPage>();
        var compare = (CompareMonthsViewModel)page.BindingContext;
        await compare.LoadAsync(Ct);

        compare.Month.ShouldBe(YearMonth.Of(Today));
        compare.Expenses.ShouldHaveSingleItem().Name.ShouldBe("Еда");
        compare.OtherIsEmpty.ShouldBeTrue();
        compare.HasForecast.ShouldBeTrue();
        compare.ForecastCards.ShouldNotBeEmpty();
        _bindingFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task CurrenciesPage_AddsAndRemoves_AndTheMenuFollows()
    {
        var page = _app.Get<CurrenciesPage>();
        var currencies = (CurrenciesViewModel)page.BindingContext;
        await currencies.LoadAsync(Ct);

        await currencies.RemoveCommand.ExecuteAsync(currencies.Chosen.Single(c => c.Code == CurrencyCode.Eur));
        await currencies.AddCommand.ExecuteAsync(currencies.Available.Single(c => c.Code == CurrencyCode.Byn));

        _app.Get<MorePage>();
        currencies.Summary.ShouldBe("RUB · USD · BYN");
        (await _app.Get<IFinanceBackend>().LoadCatalogAsync(Ct)).Currencies.ShouldBe([CurrencyCode.Rub, CurrencyCode.Usd, CurrencyCode.Byn]);
        _bindingFailures.ShouldBeEmpty();
    }

    private void OnBindingFailed(object? sender, BindingBaseErrorEventArgs e) => _bindingFailures.Add(e.Message);
}
