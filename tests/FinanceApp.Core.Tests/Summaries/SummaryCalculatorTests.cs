using FinanceApp.Application.Csv;
using FinanceApp.Application.Summaries;
using FinanceApp.Core.Tests.Domain;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Summaries;

public class SummaryCalculatorTests
{
    private static readonly Category Transport = new(Guid.NewGuid(), "Транспорт", TransactionType.Expense);
    private static readonly Account Cash = new(Guid.NewGuid(), "Наличные", CurrencyCode.Rub);
    private static readonly Account Car = new(Guid.NewGuid(), "Машина", CurrencyCode.Rub, kind: AccountKind.Envelope);
    private static readonly Account Bike = new(Guid.NewGuid(), "Велосипед", CurrencyCode.Rub, kind: AccountKind.Envelope);

    private static readonly Dictionary<Guid, Category> Categories = new[]
    {
        TestData.Food, TestData.Salary, Transport,
    }.ToDictionary(c => c.Id);

    private static readonly Dictionary<Guid, Account> Accounts = new[]
    {
        TestData.Card, TestData.UsdCard, Cash, Car, Bike,
    }.ToDictionary(a => a.Id);

    private static readonly Period October = Period.Of(new YearMonth(2026, 10));
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private static Transaction Make(string date, TransactionType type, decimal amount, Category category, decimal rate = 1m, Account? account = null) =>
        Transaction.Create(D(date), type, amount, rate, category, account ?? TestData.Card, null, TestData.Now);

    private static Transaction Move(string date, decimal amount, Account from, Account to) =>
        Transaction.CreateTransfer(D(date), amount, 1m, from, to, null, null, TestData.Now);

    private static PeriodSummary Summarize(IEnumerable<Transaction> transactions, Period? period = null, DateOnly? today = null) =>
        SummaryCalculator.For(transactions, period ?? October, Categories, Accounts, today ?? Today);

    [Fact]
    public void Totals_AndBalance()
    {
        var summary = Summarize(
        [
            Make("2026-10-01", TransactionType.Income, 50_000m, TestData.Salary),
            Make("2026-10-07", TransactionType.Expense, 350m, TestData.Food),
            Make("2026-10-31", TransactionType.Expense, 1_200m, Transport),
            Move("2026-10-08", 10_000m, TestData.Card, Car),
            Make("2026-11-01", TransactionType.Expense, 999m, TestData.Food), // next month — ignored
        ]);

        summary.Current.ShouldBe(new PeriodTotals(50_000m, 1_550m, 10_000m));
        summary.Current.Balance.ShouldBe(38_450m);
    }

    [Fact]
    public void Envelopes_NetDeposits_TransfersBetweenAccountsDoNotCount()
    {
        var summary = Summarize(
        [
            Move("2026-10-02", 10_000m, TestData.Card, Car),   // into an envelope
            Move("2026-10-03", 3_000m, Car, Cash),             // taken back
            Move("2026-10-04", 5_000m, TestData.Card, Cash),   // card → cash: nothing changes
            Move("2026-10-05", 2_000m, Car, Bike),             // envelope → envelope: nothing changes
        ]);

        summary.Current.IntoEnvelopes.ShouldBe(7_000m);
        summary.Current.Income.ShouldBe(0m);
        summary.Current.Expense.ShouldBe(0m);
        summary.Current.Balance.ShouldBe(-7_000m);
    }

    [Fact]
    public void SpendingFromEnvelope_IsAnExpense_ButDoesNotReduceFreeMoneyAgain()
    {
        var summary = Summarize(
        [
            Make("2026-10-01", TransactionType.Income, 50_000m, TestData.Salary),
            Move("2026-10-02", 30_000m, TestData.Card, Bike),
            Make("2026-10-10", TransactionType.Expense, 30_000m, Transport, account: Bike), // bought the bike
        ]);

        summary.Current.Expense.ShouldBe(30_000m);
        summary.Current.ExpenseFromEnvelopes.ShouldBe(30_000m);
        summary.Current.Balance.ShouldBe(20_000m);
        summary.ExpensesByCategory.ShouldHaveSingleItem().Amount.ShouldBe(30_000m);
    }

    [Fact]
    public void ForeignCurrency_UsesFixedRate()
    {
        var summary = Summarize(
        [
            Make("2026-10-07", TransactionType.Expense, 20m, TestData.Food, rate: 92.4567m, account: TestData.UsdCard),
            Make("2026-10-07", TransactionType.Expense, 100m, TestData.Food),
        ]);

        summary.Current.Expense.ShouldBe(1_949.13m);
    }

    [Fact]
    public void ExpensesByCategory_SortedByAmount_WithPreviousPeriod()
    {
        var summary = Summarize(
        [
            Make("2026-10-07", TransactionType.Expense, 350m, TestData.Food),
            Make("2026-10-09", TransactionType.Expense, 650m, TestData.Food),
            Make("2026-10-10", TransactionType.Expense, 1_200m, Transport),
            Make("2026-09-15", TransactionType.Expense, 800m, TestData.Food),
            Make("2026-09-15", TransactionType.Income, 40_000m, TestData.Salary),
        ]);

        summary.ExpensesByCategory.Select(c => (c.CategoryName, c.Amount, c.PreviousAmount)).ShouldBe(
        [
            ("Транспорт", 1_200m, 0m),
            ("Еда", 1_000m, 800m),
        ]);
        summary.ExpensesByCategory[1].Change.ShouldBe(200m);
        summary.ShareOf(summary.ExpensesByCategory[0]).ShouldBe(1_200m / 2_200m);
        summary.Previous.ShouldBe(new PeriodTotals(40_000m, 800m, 0m));
    }

    [Fact]
    public void Year_ComparesWithPreviousYear_AndHasMonthlyBreakdownUpToToday()
    {
        var summary = Summarize(
        [
            Make("2026-02-10", TransactionType.Expense, 100m, TestData.Food),
            Make("2026-10-10", TransactionType.Expense, 300m, TestData.Food),
            Make("2025-06-10", TransactionType.Expense, 50m, TestData.Food),
        ], Period.YearOf(Today));

        summary.Current.Expense.ShouldBe(400m);
        summary.Previous!.Expense.ShouldBe(50m);
        summary.ByMonth.Count.ShouldBe(10); // January..October, empty months included
        summary.ByMonth[1].Totals.Expense.ShouldBe(100m);
        summary.ByMonth[2].Totals.ShouldBe(PeriodTotals.Empty);
    }

    [Fact]
    public void AverageDailyExpense_UsesDaysPassedInOngoingPeriod()
    {
        var summary = Summarize([Make("2026-10-03", TransactionType.Expense, 1_500m, TestData.Food)]);

        summary.AverageDailyExpense.ShouldBe(100m); // 15 days of October have passed
    }

    [Fact]
    public void AllTime_HasNoPrevious_AndStartsAtFirstTransaction()
    {
        var summary = Summarize(
        [
            Make("2026-09-20", TransactionType.Expense, 260m, TestData.Food),
            Make("2026-10-15", TransactionType.Expense, 0.01m, TestData.Food),
        ], Period.AllTime);

        summary.Previous.ShouldBeNull();
        summary.ByMonth.Select(m => m.Month.ToString()).ShouldBe(["2026-09", "2026-10"]);
        summary.AverageDailyExpense.ShouldBe(10m); // 260.01 over 26 days
    }

    [Fact]
    public void EmptyPeriod_IsZeros()
    {
        var summary = Summarize([]);

        summary.Current.ShouldBe(PeriodTotals.Empty);
        summary.ExpensesByCategory.ShouldBeEmpty();
        summary.AverageDailyExpense.ShouldBe(0m);
    }

    [Fact]
    public void ByMonth_ForCsvRowsAndTransactions_GiveSameResult()
    {
        TransactionCsvRow[] rows =
        [
            new(D("2025-10-19"), TransactionType.Income, 35_006m, "Зарплата", "Карта", null),
            new(D("2025-10-24"), TransactionType.Expense, 981m, "Еда", "Карта", null),
            new(D("2025-11-02"), TransactionType.Transfer, 6.04m, null, "Карта", null, ToAccount: "Машина"),
        ];
        Transaction[] transactions =
        [
            Make("2025-10-19", TransactionType.Income, 35_006m, TestData.Salary),
            Make("2025-10-24", TransactionType.Expense, 981m, TestData.Food),
            Move("2025-11-02", 6.04m, TestData.Card, Car),
        ];

        var fromRows = SummaryCalculator.ByMonth(rows, name => name == "Машина");
        var fromTransactions = SummaryCalculator.ByMonth(transactions, Accounts);

        fromRows.ShouldBe(fromTransactions);
        fromRows[new YearMonth(2025, 10)].Balance.ShouldBe(34_025m);
        fromRows[new YearMonth(2025, 11)].IntoEnvelopes.ShouldBe(6.04m);
    }

    [Fact]
    public void LegacySavingRows_CountAsIntoEnvelopes() =>
        SummaryCalculator.ByMonth([new TransactionCsvRow(D("2025-11-02"), TransactionType.Saving, 6.04m, "Копилка", null, null)], _ => false)
            [new YearMonth(2025, 11)].IntoEnvelopes.ShouldBe(6.04m);
}
