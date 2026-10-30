using FinanceApp.Application.Accounts;
using FinanceApp.Application.Summaries;
using FinanceApp.Core.Tests.Domain;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Summaries;

public class MonthComparisonTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly YearMonth October = new(2026, 10);
    private static readonly YearMonth September = new(2026, 9);
    private static readonly Category Cafe = new(Guid.NewGuid(), "Кафе", TransactionType.Expense);

    private static readonly Dictionary<Guid, Category> Categories = new[] { TestData.Food, TestData.Salary, Cafe }.ToDictionary(c => c.Id);
    private static readonly Dictionary<Guid, Account> Accounts = new[] { TestData.Card }.ToDictionary(a => a.Id);

    private static Transaction Spend(string date, decimal amount, Category? category = null) =>
        TestData.Expense(amount, date, null, category ?? TestData.Food);

    private static Transaction Earn(string date, decimal amount) =>
        Transaction.Create(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), TransactionType.Income, amount, 1m, TestData.Salary, TestData.Card, null, TestData.Now);

    private static MonthComparison Compare(
        IEnumerable<Transaction> transactions,
        bool samePoint = true,
        IEnumerable<RecurringOperation>? recurring = null,
        MonthLimits? limits = null,
        YearMonth? month = null,
        YearMonth? other = null) =>
        MonthComparison.For(
            transactions, month ?? October, other ?? September, samePoint, Categories, Accounts,
            recurring ?? [], limits, _ => 1m, CurrencyCode.Rub, Today);

    [Fact]
    public void SamePoint_CountsTheOtherMonthToTodaysDay()
    {
        Transaction[] all = [Spend("2026-10-05", 1_000m), Spend("2026-09-10", 800m), Spend("2026-09-25", 5_000m)];

        var fair = Compare(all);
        fair.Through.ShouldBe(15);
        fair.Totals.Expense.ShouldBe(1_000m);
        fair.OtherTotals.Expense.ShouldBe(800m);

        var whole = Compare(all, samePoint: false);
        whole.Through.ShouldBeNull();
        whole.OtherTotals.Expense.ShouldBe(5_800m);
    }

    [Fact]
    public void PastMonths_AreComparedWhole()
    {
        var comparison = Compare([Spend("2026-09-25", 5_000m)], month: September, other: new YearMonth(2026, 8));

        comparison.Through.ShouldBeNull();
        comparison.Forecast.ShouldBeNull();
        comparison.OtherHasData.ShouldBeFalse();
    }

    [Fact]
    public void Categories_FromBothMonths_TheBiggestFirst()
    {
        var comparison = Compare(
            [Spend("2026-10-02", 3_000m), Spend("2026-10-03", 9_000m, Cafe), Spend("2026-09-02", 4_000m), Earn("2026-10-01", 50_000m)],
            samePoint: false);

        comparison.Expenses.Select(c => (c.Name, c.Amount, c.OtherAmount, c.Change)).ShouldBe(
        [
            ("Кафе", 9_000m, 0m, 9_000m),
            ("Еда", 3_000m, 4_000m, -1_000m),
        ]);
        comparison.Incomes.ShouldHaveSingleItem().OtherAmount.ShouldBe(0m);
    }

    [Fact]
    public void Forecast_CountsBillsStillToCome_AndWhatCanBeSpentADay()
    {
        var rent = RecurringOperation.Create(
            TransactionType.Expense, 30_000m, TestData.Food.Id, TestData.Card.Id, null, "Аренда", RecurrenceFrequency.Monthly, new DateOnly(2026, 10, 20), TestData.Now);

        // Missed on the 10th and not recorded: it still counts, and so do the 17th, the 24th and the 31st.
        var gym = RecurringOperation.Create(
            TransactionType.Expense, 1_000m, TestData.Food.Id, TestData.Card.Id, null, "Спорт", RecurrenceFrequency.Weekly, new DateOnly(2026, 10, 10), TestData.Now);

        var forecast = Compare([Earn("2026-10-01", 100_000m), Spend("2026-10-05", 20_000m)], recurring: [rent, gym]).Forecast.ShouldNotBeNull();

        forecast.DaysLeft.ShouldBe(17);
        forecast.Planned.Select(p => p.Date.Day).ShouldBe([10, 17, 20, 24, 31]);
        forecast.PlannedExpense.ShouldBe(34_000m);
        forecast.ExpectedExpense.ShouldBe(54_000m); // the bills are more than the pace (≈ 41 333)
        forecast.ExpectedBalance.ShouldBe(46_000m);
        forecast.PerDay.ShouldBe(2_706m); // 46 000 over 17 days
    }

    [Fact]
    public void Forecast_WithoutIncome_HasNoDailyAllowance() =>
        Compare([Spend("2026-10-05", 2_000m)]).Forecast.ShouldNotBeNull().PerDay.ShouldBeNull();

    [Fact]
    public void Forecast_TellsWhenALimitRunsOut()
    {
        var food = new Category(Guid.NewGuid(), "Еда", TransactionType.Expense);
        var cafe = new Category(Guid.NewGuid(), "Кафе", TransactionType.Expense);
        var limits = new MonthLimits(
            October,
            [new LimitLine(food, 30_000m, 20_000m), new LimitLine(cafe, 10_000m, 3_000m)],
            CurrencyCode.Rub);

        var lines = Compare([], limits: limits).Forecast.ShouldNotBeNull().Limits;

        var tight = lines[0];
        tight.WillExceed.ShouldBeTrue();
        tight.RunsOutOn.ShouldBe(new DateOnly(2026, 10, 23)); // 1 333 a day, 10 000 left
        tight.PerDayLeft.ShouldBe(588m);

        var fine = lines[1];
        fine.WillExceed.ShouldBeFalse();
        fine.RunsOutOn.ShouldBeNull();
        fine.Projected.ShouldBe(6_200m);
    }
}
