using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Domain;

public class RecurringAndLimitsDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RecurringOperation Monthly(DateOnly first) =>
        RecurringOperation.Create(TransactionType.Expense, 30_000m, Guid.NewGuid(), Guid.NewGuid(), null, "Аренда", RecurrenceFrequency.Monthly, first, Now);

    [Fact]
    public void Monthly_OnThe31st_FallsOnTheLastDay_AndComesBack()
    {
        var rent = Monthly(new DateOnly(2027, 1, 31));

        rent.Advance();
        rent.NextDate.ShouldBe(new DateOnly(2027, 2, 28));
        rent.Advance();
        rent.NextDate.ShouldBe(new DateOnly(2027, 3, 31));
        rent.Advance();
        rent.NextDate.ShouldBe(new DateOnly(2027, 4, 30));
    }

    [Fact]
    public void Weekly_AndYearly()
    {
        RecurringOperation.After(new DateOnly(2026, 10, 30), RecurrenceFrequency.Weekly, 5).ShouldBe(new DateOnly(2026, 11, 6));
        RecurringOperation.After(new DateOnly(2028, 2, 29), RecurrenceFrequency.Yearly, 29).ShouldBe(new DateOnly(2029, 2, 28));
    }

    [Fact]
    public void IsDue_OnTheDayAndAfter()
    {
        var rent = Monthly(new DateOnly(2026, 11, 5));

        rent.IsDue(new DateOnly(2026, 11, 4)).ShouldBeFalse();
        rent.IsDue(new DateOnly(2026, 11, 5)).ShouldBeTrue();
        rent.IsDue(new DateOnly(2026, 11, 20)).ShouldBeTrue();
    }

    [Fact]
    public void Transfer_NeedsTwoAccounts_AndNoCategory()
    {
        var account = Guid.NewGuid();

        Should.Throw<DomainException>(() => RecurringOperation.Create(TransactionType.Transfer, 100m, null, account, account, null, RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 1), Now));
        Should.Throw<DomainException>(() => RecurringOperation.Create(TransactionType.Transfer, 100m, Guid.NewGuid(), account, Guid.NewGuid(), null, RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 1), Now));
        Should.Throw<DomainException>(() => RecurringOperation.Create(TransactionType.Expense, 100m, null, account, null, null, RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 1), Now));
        Should.Throw<DomainException>(() => RecurringOperation.Create(TransactionType.Expense, 0m, Guid.NewGuid(), account, null, null, RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 1), Now));
    }

    [Fact]
    public void Limit_OnlyOnExpenses_AndPositive()
    {
        var food = new Category(Guid.NewGuid(), "Еда", TransactionType.Expense);
        var salary = new Category(Guid.NewGuid(), "Зарплата", TransactionType.Income);

        food.SetLimit(30_000m);
        food.MonthlyLimit.ShouldBe(30_000m);
        food.SetLimit(null);
        food.MonthlyLimit.ShouldBeNull();
        Should.Throw<DomainException>(() => food.SetLimit(0m));
        Should.Throw<DomainException>(() => food.SetLimit(10.001m));
        Should.Throw<DomainException>(() => salary.SetLimit(1000m));
    }
}
