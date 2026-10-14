using FinanceApp.Application.Export;
using FinanceApp.Core.Tests.Domain;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Export;

public class CsvExportServiceTests
{
    [Fact]
    public void BuildRows_UsesBaseAmountAndNames_KeepsOriginalCurrency()
    {
        var usd = TestData.Expense(amount: 20m, rate: 92.4567m, account: TestData.UsdCard, note: "ужин");

        var row = CsvExportService.BuildRows([usd], [TestData.Food], [TestData.UsdCard]).ShouldHaveSingleItem();

        row.Amount.ShouldBe(1849.13m);
        row.Category.ShouldBe("Еда");
        row.Account.ShouldBe("Карта USD");
        row.Currency.ShouldBe(CurrencyCode.Usd);
        row.OriginalAmount.ShouldBe(20m);
        row.Id.ShouldBe(usd.Id);
    }

    [Fact]
    public void BuildRows_SortedByDateThenCreation()
    {
        var late = Transaction.Create(new DateOnly(2026, 10, 7), TransactionType.Expense, 2m, 1m, TestData.Food, TestData.Card, "b", TestData.Now.AddMinutes(5));
        var early = Transaction.Create(new DateOnly(2026, 10, 7), TransactionType.Expense, 1m, 1m, TestData.Food, TestData.Card, "a", TestData.Now);
        var previousDay = Transaction.Create(new DateOnly(2026, 10, 6), TransactionType.Expense, 3m, 1m, TestData.Food, TestData.Card, "c", TestData.Now.AddDays(1));

        var rows = CsvExportService.BuildRows([late, early, previousDay], [TestData.Food], [TestData.Card]);

        rows.Select(r => r.Note).ShouldBe(["c", "a", "b"]);
    }
}
