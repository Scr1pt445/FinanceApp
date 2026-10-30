using FinanceApp.Application.Accounts;
using FinanceApp.Application.Debts;
using FinanceApp.Application.Transactions;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests.Recurring;

/// <summary>Debts and the month comparison on a real database, migrations included.</summary>
public sealed class DebtsAndComparisonTests : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private TestApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await TestApp.CreateAsync(exportEnabled: false);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Debts_AreStored_RepaidAndListedOpenFirst()
    {
        Guid sasha;
        await using (var scope = _app.Scope())
        {
            var service = scope.Get<DebtService>();
            sasha = (await service.AddAsync(new DebtInput(DebtDirection.Lent, "Саша", 5_000m, CurrencyCode.Rub, Today, Today.AddDays(7), "на билеты"), Ct)).Id;
            await service.AddAsync(new DebtInput(DebtDirection.Borrowed, "Банк", 100m, CurrencyCode.Usd, Today, Today.AddDays(2), null), Ct);
            var closed = await service.AddAsync(new DebtInput(DebtDirection.Lent, "Петя", 300m, CurrencyCode.Rub, Today, null, null), Ct);
            await service.RepayAsync(closed.Id, 300m, Ct);
        }

        await using (var scope = _app.Scope())
        {
            var service = scope.Get<DebtService>();
            await service.RepayAsync(sasha, 2_000m, Ct);
            var all = await service.ListAsync(Ct);

            all.Select(d => d.Person).ShouldBe(["Банк", "Саша", "Петя"]); // the soonest due first, the closed last
            var debt = all[1];
            debt.Left.ShouldBe(3_000m);
            debt.Note.ShouldBe("на билеты");
            debt.DueDate.ShouldBe(Today.AddDays(7));
            all[0].Currency.ShouldBe(CurrencyCode.Usd);
            all[2].ClosedOn.ShouldBe(Today);
        }

        await using (var scope = _app.Scope())
        {
            var service = scope.Get<DebtService>();
            await service.DeleteAsync(sasha, Ct);
            (await service.ListAsync(Ct)).Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Comparison_LoadsBothMonths_AndForecastsTheCurrentOne()
    {
        Catalog catalog;
        await using (var scope = _app.Scope())
        {
            catalog = await scope.Get<CatalogService>().LoadAsync(Ct);
        }

        var food = catalog.Categories.Single(c => c.Name == "Еда" && c.Type == TransactionType.Expense);
        var card = catalog.DefaultAccount(TransactionType.Expense)!;
        var month = YearMonth.Of(Today);
        await using (var scope = _app.Scope())
        {
            var transactions = scope.Get<TransactionService>();
            await transactions.AddAsync(new TransactionInput(month.FirstDay, TransactionType.Expense, 1_200m, food.Id, card.Id, null), Ct);
            await transactions.AddAsync(new TransactionInput(month.Previous.FirstDay, TransactionType.Expense, 1_000m, food.Id, card.Id, null), Ct);
        }

        await using (var scope = _app.Scope())
        {
            var comparison = await scope.Get<MonthComparisonService>().LoadAsync(month, month.Previous, samePoint: true, Ct);

            comparison.Totals.Expense.ShouldBe(1_200m);
            comparison.OtherTotals.Expense.ShouldBe(1_000m);
            comparison.Expenses.ShouldHaveSingleItem().Change.ShouldBe(200m);
            comparison.Forecast.ShouldNotBeNull().DaysLeft.ShouldBe(month.LastDay.Day - Today.Day + 1);
        }
    }
}
