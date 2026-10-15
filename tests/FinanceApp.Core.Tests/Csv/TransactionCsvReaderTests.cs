using System.Text;
using FinanceApp.Application.Csv;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Csv;

public class TransactionCsvReaderTests
{
    [Fact]
    public void Reads_ArchiveShapedFile_WithEmptyAccounts()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "archive-sample.csv"));

        var result = TransactionCsvReader.ReadText(text);

        result.Errors.ShouldBeEmpty();
        result.Rows.Count.ShouldBe(6);
        var first = result.Rows[0];
        first.Date.ShouldBe(new DateOnly(2025, 10, 19));
        first.Type.ShouldBe(TransactionType.Income);
        first.Amount.ShouldBe(35006m);
        first.Category.ShouldBe("Зарплата");
        first.Account.ShouldBeNull();
        first.Note.ShouldBe("импорт v1");
        first.Currency.ShouldBeNull();
        first.Id.ShouldBeNull();
        first.LineNumber.ShouldBe(2);
        result.Rows.ShouldContain(r => r.Amount == 2485.56m && r.Category == "Фриланс");
    }

    [Fact]
    public void Reads_ContractExample()
    {
        const string text = """
            date,type,amount,category,account,note
            2026-10-07,expense,350,Еда,Карта,кофе
            2026-10-07,income,50000,Зарплата,Карта,
            2026-10-08,saving,10000,Копилка,Вклад,
            """;

        var result = TransactionCsvReader.ReadText(text);

        result.Errors.ShouldBeEmpty();
        result.Rows.Select(r => r.Type).ShouldBe([TransactionType.Expense, TransactionType.Income, TransactionType.Saving]);
        result.Rows[1].Note.ShouldBeNull();
    }

    [Fact]
    public void Tolerates_Bom_Crlf_ReorderedAndUnknownColumns()
    {
        const string text = "﻿note,amount,extra,date,type\r\n\"кофе, круассан\",350.50,x,2026-10-07,expense\r\n";

        var result = TransactionCsvReader.ReadText(text);

        result.Errors.ShouldBeEmpty();
        var row = result.Rows.ShouldHaveSingleItem();
        row.Note.ShouldBe("кофе, круассан");
        row.Amount.ShouldBe(350.50m);
        row.Category.ShouldBeNull();
    }

    [Fact]
    public void Reads_QuotedFieldsWithQuotesAndLineBreaks()
    {
        const string text = "date,type,amount,note\n2026-10-07,expense,1,\"кафе \"\"Ромашка\"\"\nвторая строка\"\n2026-10-08,expense,2,\n";

        var result = TransactionCsvReader.ReadText(text);

        result.Errors.ShouldBeEmpty();
        result.Rows[0].Note.ShouldBe("кафе \"Ромашка\"\nвторая строка");
        result.Rows[1].LineNumber.ShouldBe(4);
    }

    [Theory]
    [InlineData("07.10.2026,expense,350", "дата")]
    [InlineData("2026-10-07,Expense,350", "тип")]
    [InlineData("2026-10-07,move,350", "тип")]
    [InlineData("2026-10-07,transfer,350", "to_account")]
    [InlineData("2026-10-07,expense,350,50", null)] // extra unnamed field is ignored
    [InlineData("2026-10-07,expense,\"350,50\"", "сумма")]
    [InlineData("2026-10-07,expense,-350", "сумма")]
    [InlineData("2026-10-07,expense,0", "сумма")]
    [InlineData("2026-10-07,expense,1e3", "сумма")]
    [InlineData("2026-10-07,expense,1 000", "сумма")]
    [InlineData("2026-10-07,expense,10.005", "двух знаков")]
    public void InvalidValues_AreReportedWithLineNumber(string line, string? expectedError)
    {
        var result = TransactionCsvReader.ReadText("date,type,amount\n" + line + "\n");

        if (expectedError is null)
        {
            result.Errors.ShouldBeEmpty();
            return;
        }

        var error = result.Errors.ShouldHaveSingleItem();
        error.LineNumber.ShouldBe(2);
        error.Message.ShouldContain(expectedError);
        result.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void Reads_TransferRows()
    {
        var result = TransactionCsvReader.ReadText(
            "date,type,amount,account,to_account,to_amount\n2026-10-15,transfer,10000,Карта,Машина,\n2026-10-15,transfer,9000,Карта,Наличные USD,100\n");

        result.Errors.ShouldBeEmpty();
        result.Rows[0].ToAccount.ShouldBe("Машина");
        result.Rows[0].ToAmount.ShouldBeNull();
        result.Rows[1].ToAmount.ShouldBe(100m);
    }

    [Fact]
    public void GoodRows_AreKeptWhenOtherRowsFail()
    {
        var result = TransactionCsvReader.ReadText("date,type,amount\n2026-10-07,expense,1\nbad,expense,2\n2026-10-08,income,3\n");

        result.Rows.Count.ShouldBe(2);
        result.Errors.ShouldHaveSingleItem().LineNumber.ShouldBe(3);
    }

    [Fact]
    public void MissingRequiredColumn_IsAFileError()
    {
        var result = TransactionCsvReader.ReadText("date,type,category\n2026-10-07,expense,Еда\n");

        result.Errors.ShouldHaveSingleItem().Message.ShouldContain("amount");
    }

    [Fact]
    public void EmptyFile_IsAnError() => TransactionCsvReader.ReadText("").HasErrors.ShouldBeTrue();

    [Fact]
    public void UnterminatedQuote_IsAnError() =>
        TransactionCsvReader.ReadText("date,type,amount,note\n2026-10-07,expense,1,\"кофе\n").Errors
            .ShouldHaveSingleItem().Message.ShouldContain("кавычка");

    [Fact]
    public void OriginalAmountWithoutCurrency_IsAnError() =>
        TransactionCsvReader.ReadText("date,type,amount,original_amount\n2026-10-07,expense,1845.3,20\n").Errors
            .ShouldHaveSingleItem().Message.ShouldContain("currency");

    [Fact]
    public void RoundTrip_WriterThenReader_PreservesEverything()
    {
        TransactionCsvRow[] rows =
        [
            new(new DateOnly(2026, 10, 7), TransactionType.Expense, 1845.30m, "Еда", "Карта USD", "ужин, \"Ромашка\"", CurrencyCode.Usd, 20m, Guid.NewGuid()),
            new(new DateOnly(2026, 10, 8), TransactionType.Saving, 10000m, "Копилка", "Вклад", null, CurrencyCode.Rub, 10000m, Guid.NewGuid()),
        ];

        var text = Encoding.UTF8.GetString(TransactionCsvWriter.WriteToBytes(rows));
        var result = TransactionCsvReader.ReadText(text);

        result.Errors.ShouldBeEmpty();
        result.Rows.Select(r => r with { LineNumber = 0 }).ShouldBe(rows);
    }
}
