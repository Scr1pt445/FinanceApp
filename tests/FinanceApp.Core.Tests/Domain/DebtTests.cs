using FinanceApp.Application.Debts;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Domain;

public class DebtTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);

    private static Debt Lent(decimal amount = 5_000m, DateOnly? due = null) =>
        Debt.Create(DebtDirection.Lent, "  Саша ", amount, CurrencyCode.Rub, Day, due, null, TestData.Now);

    [Fact]
    public void Create_TrimsTheName_AndStartsOpen()
    {
        var debt = Lent();

        debt.Person.ShouldBe("Саша");
        debt.Left.ShouldBe(5_000m);
        debt.IsClosed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("Саша", 0)]
    [InlineData("Саша", -5)]
    [InlineData("Саша", 10.555)]
    public void Create_RejectsAMissingNameOrABadAmount(string person, decimal amount) =>
        Should.Throw<DomainException>(() => Debt.Create(DebtDirection.Lent, person, amount, CurrencyCode.Rub, Day, null, null, TestData.Now));

    [Fact]
    public void Create_RejectsADueDateBeforeTheDay() =>
        Should.Throw<DomainException>(() => Lent(due: Day.AddDays(-1))).Message.ShouldContain("раньше");

    [Fact]
    public void Repay_InParts_ClosesWhenAllIsBack()
    {
        var debt = Lent();

        debt.Repay(2_000m, Day.AddDays(3));
        debt.Left.ShouldBe(3_000m);
        debt.IsClosed.ShouldBeFalse();

        debt.Repay(3_000m, Day.AddDays(10));
        debt.IsClosed.ShouldBeTrue();
        debt.ClosedOn.ShouldBe(Day.AddDays(10));
        Should.Throw<DomainException>(() => debt.Repay(1m, Day.AddDays(11)));
    }

    [Fact]
    public void Repay_MoreThanLeft_IsRefused()
    {
        var debt = Lent();
        debt.Repay(4_000m, Day);

        Should.Throw<DomainException>(() => debt.Repay(1_500m, Day)).Message.ShouldContain("больше остатка");
    }

    [Fact]
    public void Update_CannotGoBelowWhatCameBack_AndRaisingTheSumReopens()
    {
        var debt = Lent();
        debt.Repay(5_000m, Day);

        Should.Throw<DomainException>(() => debt.Update(DebtDirection.Lent, "Саша", 4_000m, CurrencyCode.Rub, Day, null, null));
        debt.Update(DebtDirection.Lent, "Саша", 6_000m, CurrencyCode.Rub, Day, null, null);

        debt.IsClosed.ShouldBeFalse();
        debt.Left.ShouldBe(1_000m);
    }

    [Fact]
    public void Overdue_OnlyWhileOpen_AndAfterTheDueDate()
    {
        var debt = Lent(due: Day.AddDays(5));

        debt.IsOverdue(Day.AddDays(5)).ShouldBeFalse();
        debt.IsOverdue(Day.AddDays(6)).ShouldBeTrue();
        debt.Repay(5_000m, Day.AddDays(6));
        debt.IsOverdue(Day.AddDays(7)).ShouldBeFalse();
    }

    [Fact]
    public void Totals_ArePerDirectionAndCurrency_WithoutClosedOnes()
    {
        var closed = Lent(1_000m);
        closed.Repay(1_000m, Day);
        var partly = Lent(5_000m);
        partly.Repay(1_000m, Day);
        var debts = new[]
        {
            closed,
            partly,
            Lent(2_000m),
            Debt.Create(DebtDirection.Lent, "Петя", 100m, CurrencyCode.Usd, Day, null, null, TestData.Now),
            Debt.Create(DebtDirection.Borrowed, "Банк", 3_000m, CurrencyCode.Rub, Day, null, null, TestData.Now),
        };

        DebtService.Totals(debts).ShouldBe(
        [
            new DebtTotal(DebtDirection.Lent, CurrencyCode.Rub, 6_000m),
            new DebtTotal(DebtDirection.Lent, CurrencyCode.Usd, 100m),
            new DebtTotal(DebtDirection.Borrowed, CurrencyCode.Rub, 3_000m),
        ]);
    }
}
