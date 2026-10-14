using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Domain;

public class TransactionTests
{
    [Fact]
    public void Create_TakesCurrencyAndIdsFromAccountAndCategory()
    {
        var transaction = TestData.Expense(account: TestData.UsdCard, rate: 92.5m, amount: 20m);

        transaction.Currency.ShouldBe(CurrencyCode.Usd);
        transaction.AccountId.ShouldBe(TestData.UsdCard.Id);
        transaction.CategoryId.ShouldBe(TestData.Food.Id);
        transaction.Id.ShouldNotBe(Guid.Empty);
        transaction.CreatedAt.ShouldBe(TestData.Now);
        transaction.UpdatedAt.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveAmount_Throws(decimal amount) =>
        Should.Throw<DomainException>(() => TestData.Expense(amount: amount));

    [Fact]
    public void Create_MoreThanTwoDecimalPlaces_Throws() =>
        Should.Throw<DomainException>(() => TestData.Expense(amount: 10.005m));

    [Fact]
    public void Create_TrailingZerosBeyondTwoPlaces_AreAccepted() =>
        TestData.Expense(amount: 10.500m).Amount.ShouldBe(10.5m);

    [Fact]
    public void Create_AmountAboveLimit_Throws() =>
        Should.Throw<DomainException>(() => TestData.Expense(amount: Transaction.MaxAmount + 1));

    [Theory]
    [InlineData(0)]
    [InlineData(-92.5)]
    public void Create_NonPositiveRate_Throws(decimal rate) =>
        Should.Throw<DomainException>(() => TestData.Expense(rate: rate));

    [Fact]
    public void Create_CategoryOfAnotherType_Throws() =>
        Should.Throw<DomainException>(() => TestData.Expense(category: TestData.Salary))
            .Message.ShouldContain("Зарплата");

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  кофе  ", "кофе")]
    public void Create_NormalizesNote(string? note, string? expected) =>
        TestData.Expense(note: note).Note.ShouldBe(expected);

    [Fact]
    public void Create_TooLongNote_Throws() =>
        Should.Throw<DomainException>(() => TestData.Expense(note: new string('x', Transaction.MaxNoteLength + 1)));

    [Theory]
    [InlineData(350, 1, 350)]
    [InlineData(20, 92.4567, 1849.13)]
    [InlineData(0.01, 0.5, 0.01)] // 0.005 rounds away from zero
    [InlineData(10, 3.3333, 33.33)]
    public void BaseAmount_IsAmountTimesRateRoundedToKopecks(decimal amount, decimal rate, decimal expected) =>
        TestData.Expense(amount: amount, rate: rate).BaseAmount.ShouldBe(expected);

    [Fact]
    public void Update_ChangesFieldsAndTouchesUpdatedAtOnly()
    {
        var transaction = TestData.Expense();
        var later = TestData.Now.AddHours(1);

        transaction.Update(
            new DateOnly(2026, 10, 8),
            TransactionType.Income,
            50_000m,
            1m,
            TestData.Salary,
            TestData.Card,
            null,
            later);

        transaction.Type.ShouldBe(TransactionType.Income);
        transaction.Amount.ShouldBe(50_000m);
        transaction.Date.ShouldBe(new DateOnly(2026, 10, 8));
        transaction.Note.ShouldBeNull();
        transaction.CreatedAt.ShouldBe(TestData.Now);
        transaction.UpdatedAt.ShouldBe(later);
    }

    [Fact]
    public void Update_InvalidData_LeavesTransactionUnchanged()
    {
        var transaction = TestData.Expense();

        Should.Throw<DomainException>(() => transaction.Update(
            new DateOnly(2026, 10, 8), TransactionType.Expense, -5m, 1m, TestData.Food, TestData.Card, null, TestData.Now));

        transaction.Amount.ShouldBe(350m);
    }
}
