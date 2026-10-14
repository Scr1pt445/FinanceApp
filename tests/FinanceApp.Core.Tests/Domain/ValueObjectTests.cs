using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Domain;

public class CurrencyCodeTests
{
    [Theory]
    [InlineData("RUB", "RUB")]
    [InlineData("usd", "USD")]
    [InlineData(" byn ", "BYN")]
    public void Parse_NormalizesToUpperCase(string input, string expected) =>
        CurrencyCode.Parse(input).Value.ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("RU")]
    [InlineData("RUBL")]
    [InlineData("РУБ")]
    [InlineData("U$D")]
    public void Parse_Invalid_Throws(string input) =>
        Should.Throw<DomainException>(() => CurrencyCode.Parse(input));

    [Fact]
    public void Default_IsRub() => default(CurrencyCode).ShouldBe(CurrencyCode.Rub);

    [Fact]
    public void Equality_IsByValue() => CurrencyCode.Parse("eur").ShouldBe(CurrencyCode.Eur);
}

public class YearMonthTests
{
    [Fact]
    public void Parse_And_ToString_RoundTrip() => YearMonth.Parse("2025-10").ToString().ShouldBe("2025-10");

    [Theory]
    [InlineData("2025-13")]
    [InlineData("2025-1")]
    [InlineData("октябрь")]
    public void Parse_Invalid_Throws(string input) => Should.Throw<DomainException>(() => YearMonth.Parse(input));

    [Fact]
    public void Previous_And_Next_CrossYearBoundary()
    {
        new YearMonth(2026, 1).Previous.ShouldBe(new YearMonth(2025, 12));
        new YearMonth(2025, 12).Next.ShouldBe(new YearMonth(2026, 1));
    }

    [Fact]
    public void LastDay_HandlesLeapYear() => new YearMonth(2028, 2).LastDay.ShouldBe(new DateOnly(2028, 2, 29));

    [Fact]
    public void Contains_ChecksYearAndMonth()
    {
        var october = new YearMonth(2025, 10);
        october.Contains(new DateOnly(2025, 10, 31)).ShouldBeTrue();
        october.Contains(new DateOnly(2024, 10, 1)).ShouldBeFalse();
    }
}

public class CategoryAndAccountTests
{
    [Fact]
    public void Category_TrimsName() => new Category(Guid.NewGuid(), "  Еда ", TransactionType.Expense).Name.ShouldBe("Еда");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Category_EmptyName_Throws(string name) =>
        Should.Throw<DomainException>(() => new Category(Guid.NewGuid(), name, TransactionType.Expense));

    [Fact]
    public void Category_HideAndShow()
    {
        var category = new Category(Guid.NewGuid(), "Еда", TransactionType.Expense);
        category.Hide();
        category.IsHidden.ShouldBeTrue();
        category.Show();
        category.IsHidden.ShouldBeFalse();
    }

    [Fact]
    public void Account_EmptyId_Throws() =>
        Should.Throw<DomainException>(() => new Account(Guid.Empty, "Карта", CurrencyCode.Rub));

    [Fact]
    public void Account_Rename_Validates()
    {
        var account = new Account(Guid.NewGuid(), "Карта", CurrencyCode.Rub);
        account.Rename(" Тинькофф ");
        account.Name.ShouldBe("Тинькофф");
        Should.Throw<DomainException>(() => account.Rename(""));
    }
}
