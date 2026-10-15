using FinanceApp.Application.Summaries;
using FinanceApp.Core.Tests.Domain;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Summaries;

public class PeriodInsightsTests
{
    private static readonly Category Rent = new(Guid.NewGuid(), "Жильё", TransactionType.Expense);
    private static readonly Category Freelance = new(Guid.NewGuid(), "Фриланс", TransactionType.Income);
    private static readonly Account Car = new(Guid.NewGuid(), "Машина", CurrencyCode.Rub, kind: AccountKind.Envelope);
    private static readonly Account Trip = new(Guid.NewGuid(), "Поездка", CurrencyCode.Usd, kind: AccountKind.Envelope);

    private static readonly Dictionary<Guid, Category> Categories = new[] { TestData.Food, TestData.Salary, Rent, Freelance }.ToDictionary(c => c.Id);
    private static readonly Dictionary<Guid, Account> Accounts = new[] { TestData.Card, Car, Trip }.ToDictionary(a => a.Id);

    private static readonly Period October = Period.Of(new YearMonth(2026, 10));
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private static Transaction Spend(string date, decimal amount, Category? category = null, Account? account = null, string? note = null) =>
        Transaction.Create(D(date), TransactionType.Expense, amount, 1m, category ?? TestData.Food, account ?? TestData.Card, note, TestData.Now);

    private static Transaction Earn(string date, decimal amount, Category? category = null) =>
        Transaction.Create(D(date), TransactionType.Income, amount, 1m, category ?? TestData.Salary, TestData.Card, null, TestData.Now);

    private static Transaction Move(string date, decimal amount, Account from, Account to, decimal? received = null) =>
        Transaction.CreateTransfer(D(date), amount, 1m, from, to, received, null, TestData.Now);

    private static PeriodInsights For(IEnumerable<Transaction> transactions, Period? period = null, DateOnly? today = null) =>
        PeriodInsights.For(transactions, period ?? October, Categories, Accounts, today ?? Today);

    [Fact]
    public void Month_IsChartedByDay_UpToToday()
    {
        var insights = For([Spend("2026-10-03", 35_000m, Rent), Spend("2026-10-05", 500m), Spend("2026-10-20", 999m)], today: Today);

        insights.BucketSize.ShouldBe(BucketSize.Day);
        insights.Buckets.Count.ShouldBe(31);
        insights.Cumulative.Count.ShouldBe(15);
        insights.Cumulative[2].ShouldBe(35_000m);
        insights.Cumulative[^1].ShouldBe(35_500m);
        insights.ElapsedDays.ShouldBe(15);
    }

    [Theory]
    [InlineData(PeriodKind.Week, BucketSize.Day, 7)]
    [InlineData(PeriodKind.Quarter, BucketSize.Week, 14)]
    [InlineData(PeriodKind.Year, BucketSize.Month, 12)]
    public void BucketSize_FollowsPeriodLength(PeriodKind kind, BucketSize size, int count)
    {
        var insights = For([], Period.Of(kind, Today));

        insights.BucketSize.ShouldBe(size);
        insights.Buckets.Count.ShouldBe(count);
        insights.Buckets[^1].End.ShouldBe(Period.Of(kind, Today).End);
    }

    [Fact]
    public void Pace_ComparedWithTheSameDayOfThePreviousPeriod()
    {
        var insights = For(
        [
            Spend("2026-09-03", 35_000m, Rent),
            Spend("2026-09-10", 2_000m),
            Spend("2026-09-25", 8_000m),
            Spend("2026-10-03", 35_000m, Rent),
            Spend("2026-10-12", 5_000m),
        ]);

        insights.PreviousToSamePoint.ShouldBe(37_000m);
        insights.PreviousCumulative.Count.ShouldBe(30);
        insights.PreviousCumulative[^1].ShouldBe(45_000m);
    }

    [Fact]
    public void Forecast_AddsWhatTheRestOfThePreviousPeriodCost()
    {
        var insights = For(
        [
            Spend("2026-09-03", 35_000m, Rent),
            Spend("2026-09-25", 8_000m),
            Spend("2026-10-03", 35_000m, Rent),
            Spend("2026-10-12", 5_000m),
        ]);

        insights.IsOngoing.ShouldBeTrue();
        insights.Forecast.ShouldBe(48_000m); // 40 000 so far + 8 000 that September spent after the 15th
    }

    [Fact]
    public void Forecast_WithoutHistory_ExtrapolatesThePaceSinceTheFirstOperation()
    {
        For([Spend("2026-10-06", 1_000m)]).Forecast.ShouldBe(2_600m); // 100 a day over 10 days, 16 days left

        // A year tracked from July is not averaged over the empty months before it.
        For([Spend("2026-07-01", 10_700m)], Period.YearOf(Today)).Forecast.ShouldBe(18_400m); // 100 a day over 107 days, 77 left
    }

    [Fact]
    public void EmptyPreviousPeriod_IsNoHistory_NotZeros()
    {
        var insights = For([Spend("2026-10-05", 1_500m)]);

        insights.PreviousCumulative.ShouldBeEmpty();
        insights.PreviousToSamePoint.ShouldBeNull();
    }

    [Fact]
    public void FinishedPeriod_HasNoForecast()
    {
        var insights = For([Spend("2026-09-05", 100m)], Period.Of(new YearMonth(2026, 9)));

        insights.IsOngoing.ShouldBeFalse();
        insights.Forecast.ShouldBeNull();
        insights.ElapsedDays.ShouldBe(30);
        insights.Cumulative.Count.ShouldBe(30);
    }

    [Fact]
    public void SpentFromAccounts_LeavesOutSpendingFromEnvelopes()
    {
        var insights = For(
        [
            Spend("2026-10-03", 35_000m, Rent),
            Spend("2026-10-05", 500m),
            Spend("2026-10-06", 18_000m, account: Car),
        ]);

        insights.SpentFromAccountsByCategory.Select(c => (c.CategoryName, c.Amount)).ShouldBe([("Жильё", 35_000m), ("Еда", 500m)]);
    }

    [Fact]
    public void TopExpenses_AndIncomeSources()
    {
        var insights = For(
        [
            Spend("2026-10-03", 35_000m, Rent, note: "аренда"),
            Spend("2026-10-05", 500m),
            Spend("2026-10-06", 6_500m),
            Earn("2026-10-10", 60_000m),
            Earn("2026-10-07", 25_000m, Freelance),
            Earn("2026-10-12", 5_000m, Freelance),
        ]);

        insights.TopExpenses.Select(t => t.Amount).ShouldBe([35_000m, 6_500m, 500m]);
        insights.IncomeByCategory.Select(c => (c.CategoryName, c.Amount)).ShouldBe([("Зарплата", 60_000m), ("Фриланс", 30_000m)]);
    }

    [Fact]
    public void EnvelopeFlows_InTheEnvelopeCurrency()
    {
        var insights = For(
        [
            Move("2026-10-11", 15_000m, TestData.Card, Car),
            Spend("2026-10-12", 4_000m, account: Car),
            Move("2026-10-02", 9_200m, TestData.Card, Trip, received: 100m),
            Move("2026-09-11", 15_000m, TestData.Card, Car),
        ]);

        insights.EnvelopeFlows[Car.Id].ShouldBe(11_000m);
        insights.EnvelopeFlows[Trip.Id].ShouldBe(100m);
    }

    [Fact]
    public void AllTime_StartsAtTheFirstTransaction_ByMonth()
    {
        var insights = For([Spend("2026-02-10", 100m), Spend("2026-10-10", 300m)], Period.AllTime);

        insights.BucketSize.ShouldBe(BucketSize.Month);
        insights.Buckets[0].Start.ShouldBe(new DateOnly(2026, 2, 10));
        insights.Buckets.Count.ShouldBe(9);
        insights.PreviousCumulative.ShouldBeEmpty();
        insights.PreviousToSamePoint.ShouldBeNull();
        insights.Forecast.ShouldBeNull();
    }
}
