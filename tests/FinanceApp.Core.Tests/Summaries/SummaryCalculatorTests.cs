using FinanceApp.Application.Csv;
using FinanceApp.Application.Summaries;
using FinanceApp.Core.Tests.Domain;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Summaries;

public class SummaryCalculatorTests
{
    private static readonly Category Transport = new(Guid.NewGuid(), "Транспорт", TransactionType.Expense);

    private static readonly Dictionary<Guid, Category> Categories = new[]
    {
        TestData.Food, TestData.Salary, TestData.PiggyBank, Transport,
    }.ToDictionary(c => c.Id);

    private static readonly YearMonth October = new(2026, 10);

    private static Transaction Make(string date, TransactionType type, decimal amount, Category category, decimal rate = 1m, Account? account = null) =>
        Transaction.Create(
            DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            type,
            amount,
            rate,
            category,
            account ?? TestData.Card,
            null,
            TestData.Now);

    [Fact]
    public void ForMonth_ComputesTotalsAndBalance()
    {
        Transaction[] transactions =
        [
            Make("2026-10-01", TransactionType.Income, 50_000m, TestData.Salary),
            Make("2026-10-07", TransactionType.Expense, 350m, TestData.Food),
            Make("2026-10-31", TransactionType.Expense, 1_200m, Transport),
            Make("2026-10-08", TransactionType.Saving, 10_000m, TestData.PiggyBank),
            Make("2026-11-01", TransactionType.Expense, 999m, TestData.Food), // next month — ignored
        ];

        var summary = SummaryCalculator.ForMonth(transactions, October, Categories);

        summary.Current.ShouldBe(new MonthTotals(50_000m, 1_550m, 10_000m));
        summary.Current.Balance.ShouldBe(38_450m);
    }

    [Fact]
    public void ForMonth_ConvertsForeignCurrencyWithFixedRate()
    {
        Transaction[] transactions =
        [
            Make("2026-10-07", TransactionType.Expense, 20m, TestData.Food, rate: 92.4567m, account: TestData.UsdCard),
            Make("2026-10-07", TransactionType.Expense, 100m, TestData.Food),
        ];

        SummaryCalculator.ForMonth(transactions, October, Categories).Current.Expense.ShouldBe(1_949.13m);
    }

    [Fact]
    public void ForMonth_GroupsExpensesByCategory_SortedByAmount_WithPreviousMonth()
    {
        Transaction[] transactions =
        [
            Make("2026-10-07", TransactionType.Expense, 350m, TestData.Food),
            Make("2026-10-09", TransactionType.Expense, 650m, TestData.Food),
            Make("2026-10-10", TransactionType.Expense, 1_200m, Transport),
            Make("2026-09-15", TransactionType.Expense, 800m, TestData.Food),
            Make("2026-09-15", TransactionType.Income, 40_000m, TestData.Salary),
        ];

        var summary = SummaryCalculator.ForMonth(transactions, October, Categories);

        summary.ExpensesByCategory.Select(c => (c.CategoryName, c.Amount, c.PreviousAmount)).ShouldBe(
        [
            ("Транспорт", 1_200m, 0m),
            ("Еда", 1_000m, 800m),
        ]);
        summary.ExpensesByCategory[1].Change.ShouldBe(200m);
        summary.ShareOf(summary.ExpensesByCategory[0]).ShouldBe(1_200m / 2_200m);
        summary.Previous.ShouldBe(new MonthTotals(40_000m, 800m, 0m));
    }

    [Fact]
    public void ForMonth_CategoryOnlyInPreviousMonth_IsListedWithZero()
    {
        var summary = SummaryCalculator.ForMonth(
            [Make("2026-09-15", TransactionType.Expense, 800m, TestData.Food)], October, Categories);

        var food = summary.ExpensesByCategory.ShouldHaveSingleItem();
        food.Amount.ShouldBe(0m);
        food.PreviousAmount.ShouldBe(800m);
    }

    [Fact]
    public void ForMonth_January_ComparesWithDecemberOfPreviousYear()
    {
        var summary = SummaryCalculator.ForMonth(
            [Make("2025-12-31", TransactionType.Income, 1m, TestData.Salary)], new YearMonth(2026, 1), Categories);

        summary.Previous.Income.ShouldBe(1m);
    }

    [Fact]
    public void ForMonth_EmptyMonth_IsZeros()
    {
        var summary = SummaryCalculator.ForMonth([], October, Categories);

        summary.Current.ShouldBe(MonthTotals.Empty);
        summary.ExpensesByCategory.ShouldBeEmpty();
        summary.Current.Balance.ShouldBe(0m);
    }

    [Fact]
    public void ByMonth_ForCsvRowsAndTransactions_GiveSameResult()
    {
        TransactionCsvRow[] rows =
        [
            new(new DateOnly(2025, 10, 19), TransactionType.Income, 35_006m, "Зарплата", null, null),
            new(new DateOnly(2025, 10, 24), TransactionType.Expense, 981m, "Еда", null, null),
            new(new DateOnly(2025, 11, 2), TransactionType.Saving, 6.04m, "Копилка", null, null),
        ];
        Transaction[] transactions =
        [
            Make("2025-10-19", TransactionType.Income, 35_006m, TestData.Salary),
            Make("2025-10-24", TransactionType.Expense, 981m, TestData.Food),
            Make("2025-11-02", TransactionType.Saving, 6.04m, TestData.PiggyBank),
        ];

        var fromRows = SummaryCalculator.ByMonth(rows);
        var fromTransactions = SummaryCalculator.ByMonth(transactions);

        fromRows.ShouldBe(fromTransactions);
        fromRows.Keys.ShouldBe([new YearMonth(2025, 10), new YearMonth(2025, 11)]);
        fromRows[new YearMonth(2025, 10)].Balance.ShouldBe(34_025m);
    }
}
