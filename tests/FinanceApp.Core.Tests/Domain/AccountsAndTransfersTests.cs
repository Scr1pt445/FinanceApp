using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Domain;

public class AccountKindsTests
{
    [Fact]
    public void NewAccount_IsRegular_WithZeroOpeningBalance()
    {
        var account = new Account(Guid.NewGuid(), "Карта", CurrencyCode.Rub);

        account.Kind.ShouldBe(AccountKind.Regular);
        account.IsEnvelope.ShouldBeFalse();
        account.OpeningBalance.ShouldBe(0m);
        account.GoalAmount.ShouldBeNull();
    }

    [Fact]
    public void OpeningBalance_MayBeNegative_ButNotFractionalKopecks()
    {
        var account = new Account(Guid.NewGuid(), "Кредитка", CurrencyCode.Rub, openingBalance: -1500.50m);

        account.OpeningBalance.ShouldBe(-1500.50m);
        Should.Throw<DomainException>(() => account.SetOpeningBalance(1.005m));
    }

    [Fact]
    public void Envelope_GoalIsOptional_AndCanBeCleared()
    {
        var car = new Account(Guid.NewGuid(), "Машина", CurrencyCode.Rub, kind: AccountKind.Envelope);

        car.SetGoal(150_000m, new DateOnly(2027, 5, 1));
        car.GoalAmount.ShouldBe(150_000m);
        car.GoalDate.ShouldBe(new DateOnly(2027, 5, 1));

        car.ClearGoal();
        car.GoalAmount.ShouldBeNull();
        car.GoalDate.ShouldBeNull();
    }

    [Fact]
    public void Goal_OnlyForEnvelopes_AndPositive()
    {
        var card = new Account(Guid.NewGuid(), "Карта", CurrencyCode.Rub);
        var car = new Account(Guid.NewGuid(), "Машина", CurrencyCode.Rub, kind: AccountKind.Envelope);

        Should.Throw<DomainException>(() => card.SetGoal(1000m, null));
        Should.Throw<DomainException>(() => car.SetGoal(0m, null));
    }
}

public class TransferTests
{
    private static readonly Account Card = new(Guid.NewGuid(), "Карта", CurrencyCode.Rub);
    private static readonly Account Car = new(Guid.NewGuid(), "Машина", CurrencyCode.Rub, kind: AccountKind.Envelope);
    private static readonly Account UsdCash = new(Guid.NewGuid(), "Наличные USD", CurrencyCode.Usd);

    private static Transaction Transfer(Account from, Account to, decimal amount, decimal? toAmount = null, decimal rate = 1m) =>
        Transaction.CreateTransfer(new DateOnly(2026, 10, 15), amount, rate, from, to, toAmount, "в конверт", TestData.Now);

    [Fact]
    public void SameCurrency_ReceivesTheSameAmount_NoCategory()
    {
        var t = Transfer(Card, Car, 10_000m);

        t.Type.ShouldBe(TransactionType.Transfer);
        t.IsTransfer.ShouldBeTrue();
        t.CategoryId.ShouldBeNull();
        t.AccountId.ShouldBe(Card.Id);
        t.ToAccountId.ShouldBe(Car.Id);
        t.ToAmount.ShouldBe(10_000m);
        t.Currency.ShouldBe(CurrencyCode.Rub);
    }

    [Fact]
    public void SameCurrency_DifferentReceivedAmount_IsRejected() =>
        Should.Throw<DomainException>(() => Transfer(Card, Car, 10_000m, toAmount: 9_000m));

    [Fact]
    public void DifferentCurrencies_RequireReceivedAmount()
    {
        Should.Throw<DomainException>(() => Transfer(Card, UsdCash, 9_000m))
            .Message.ShouldContain("USD");

        Transfer(Card, UsdCash, 9_000m, toAmount: 100m).ToAmount.ShouldBe(100m);
    }

    [Fact]
    public void ToTheSameAccount_IsRejected() =>
        Should.Throw<DomainException>(() => Transfer(Card, Card, 100m));

    [Fact]
    public void IncomeOrExpense_CannotBeCreatedAsTransfer()
    {
        var food = new Category(Guid.NewGuid(), "Еда", TransactionType.Expense);

        Should.Throw<DomainException>(() => Transaction.Create(
            new DateOnly(2026, 10, 15), TransactionType.Transfer, 1m, 1m, food, Card, null, TestData.Now));
    }

    [Fact]
    public void EffectOn_MovesMoneyBetweenAccounts()
    {
        var transfer = Transfer(Card, UsdCash, 9_000m, toAmount: 100m, rate: 1m);
        var expense = Transaction.Create(new DateOnly(2026, 10, 15), TransactionType.Expense, 350m, 1m,
            new Category(Guid.NewGuid(), "Еда", TransactionType.Expense), Card, null, TestData.Now);

        transfer.EffectOn(Card.Id).ShouldBe(-9_000m);
        transfer.EffectOn(UsdCash.Id).ShouldBe(100m);
        transfer.EffectOn(Car.Id).ShouldBe(0m);
        expense.EffectOn(Card.Id).ShouldBe(-350m);
    }

    [Fact]
    public void UpdateTransfer_ChangesDestination()
    {
        var t = Transfer(Card, Car, 1_000m);
        var bike = new Account(Guid.NewGuid(), "Велосипед", CurrencyCode.Rub, kind: AccountKind.Envelope);

        t.UpdateTransfer(t.Date, 2_000m, 1m, Card, bike, null, null, TestData.Now.AddHours(1));

        t.ToAccountId.ShouldBe(bike.Id);
        t.ToAmount.ShouldBe(2_000m);
        t.Note.ShouldBeNull();
    }
}

public class PeriodTests
{
    // Thursday.
    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public void Week_IsMondayToSunday()
    {
        var week = Period.WeekOf(Day);

        week.Start.ShouldBe(new DateOnly(2026, 10, 12));
        week.End.ShouldBe(new DateOnly(2026, 10, 18));
        week.Previous!.Start.ShouldBe(new DateOnly(2026, 10, 5));
    }

    [Fact]
    public void Quarter_And_Year()
    {
        var quarter = Period.QuarterOf(Day);
        quarter.Start.ShouldBe(new DateOnly(2026, 10, 1));
        quarter.End.ShouldBe(new DateOnly(2026, 12, 31));
        quarter.Previous!.Start.ShouldBe(new DateOnly(2026, 7, 1));

        var year = Period.YearOf(Day);
        year.Next!.Start.ShouldBe(new DateOnly(2027, 1, 1));
        year.Days.ShouldBe(365);
    }

    [Fact]
    public void Month_PreviousCrossesYear()
    {
        var january = Period.MonthOf(new DateOnly(2026, 1, 20));

        january.Previous!.Start.ShouldBe(new DateOnly(2025, 12, 1));
        january.Previous.End.ShouldBe(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public void Custom_StepsByItsLength()
    {
        var custom = Period.Custom(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));

        custom.Days.ShouldBe(10);
        custom.Previous!.Start.ShouldBe(new DateOnly(2026, 9, 21));
        custom.Previous.End.ShouldBe(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public void AllTime_HasNoNeighbours_AndContainsEverything()
    {
        Period.AllTime.Previous.ShouldBeNull();
        Period.AllTime.Contains(new DateOnly(1999, 1, 1)).ShouldBeTrue();
    }

    [Fact]
    public void EndBeforeStart_IsRejected() =>
        Should.Throw<DomainException>(() => Period.Custom(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1)));

    [Fact]
    public void Months_AreClampedToTheGivenRange()
    {
        var year = Period.YearOf(Day);

        year.Months(clampEnd: Day).Count().ShouldBe(10);
        Period.AllTime.Months(new DateOnly(2025, 11, 5), Day).Select(m => m.ToString())
            .ShouldBe(["2025-11", "2025-12", "2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"]);
    }
}
