using System.Globalization;
using System.Text;
using FinanceApp.Application.Csv;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Csv;

public class TransactionCsvWriterTests
{
    private static readonly DateOnly Oct7 = new(2026, 10, 7);

    private static string WriteToString(params TransactionCsvRow[] rows) =>
        Encoding.UTF8.GetString(TransactionCsvWriter.WriteToBytes(rows));

    [Fact]
    public void Header_StartsWithObsidianContractColumnsInOrder()
    {
        var text = WriteToString();

        text.ShouldBe("date,type,amount,category,account,note,currency,original_amount,id\n");
        text.ShouldStartWith("date,type,amount,category,account,note,");
    }

    [Fact]
    public void Output_HasNoBom()
    {
        var bytes = TransactionCsvWriter.WriteToBytes([new TransactionCsvRow(Oct7, TransactionType.Expense, 350m, "Еда", "Карта", "кофе")]);

        bytes[..4].ShouldBe("date"u8.ToArray());
    }

    [Fact]
    public void Output_UsesLfLineEndings()
    {
        var text = WriteToString(new TransactionCsvRow(Oct7, TransactionType.Expense, 350m, "Еда", "Карта", "кофе"));

        text.ShouldNotContain("\r");
        text.Split('\n').Length.ShouldBe(3); // header, row, trailing empty
    }

    [Fact]
    public void Row_MatchesContractExample()
    {
        var id = Guid.Parse("0b6c2f8e-0000-7000-8000-000000000001");
        var text = WriteToString(
            new TransactionCsvRow(Oct7, TransactionType.Expense, 350m, "Еда", "Карта", "кофе", CurrencyCode.Rub, 350m, id),
            new TransactionCsvRow(Oct7, TransactionType.Income, 50000m, "Зарплата", "Карта", null),
            new TransactionCsvRow(new DateOnly(2026, 10, 8), TransactionType.Saving, 10000m, "Копилка", "Вклад", null));

        var lines = text.Split('\n');
        lines[1].ShouldBe("2026-10-07,expense,350,Еда,Карта,кофе,RUB,350,0b6c2f8e-0000-7000-8000-000000000001");
        lines[2].ShouldBe("2026-10-07,income,50000,Зарплата,Карта,,,,");
        lines[3].ShouldBe("2026-10-08,saving,10000,Копилка,Вклад,,,,");
    }

    [Theory]
    [InlineData("350", "350")]
    [InlineData("350.5", "350.50")]
    [InlineData("350.50", "350.50")]
    [InlineData("350.00", "350")]
    [InlineData("0.01", "0.01")]
    [InlineData("2485.56", "2485.56")]
    [InlineData("1000000000000", "1000000000000")]
    [InlineData("12345678.9", "12345678.90")]
    public void FormatAmount_DotDecimalNoExponentNoGrouping(string input, string expected) =>
        TransactionCsvFormat.FormatAmount(decimal.Parse(input, CultureInfo.InvariantCulture)).ShouldBe(expected);

    [Fact]
    public void FormatAmount_IgnoresCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            TransactionCsvFormat.FormatAmount(1234.5m).ShouldBe("1234.50");
            TransactionCsvFormat.FormatDate(Oct7).ShouldBe("2026-10-07");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("кофе", "кофе")]
    [InlineData("кофе, круассан", "\"кофе, круассан\"")]
    [InlineData("кафе \"Ромашка\"", "\"кафе \"\"Ромашка\"\"\"")]
    [InlineData("строка\nдругая", "\"строка\nдругая\"")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Escape_QuotesOnlyWhenNeeded(string? input, string expected) =>
        TransactionCsvWriter.Escape(input).ShouldBe(expected);

    [Fact]
    public void NoteWithComma_IsQuotedInRow()
    {
        var text = WriteToString(new TransactionCsvRow(Oct7, TransactionType.Expense, 350m, "Еда", "Карта", "кофе, круассан"));

        text.Split('\n')[1].ShouldBe("2026-10-07,expense,350,Еда,Карта,\"кофе, круассан\",,,");
    }
}
