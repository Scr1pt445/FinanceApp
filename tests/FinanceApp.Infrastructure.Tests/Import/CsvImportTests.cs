using System.Text;
using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Csv;
using FinanceApp.Application.Export;
using FinanceApp.Application.Import;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests.Import;

public class CsvImportTests
{
    private static readonly string ArchiveText =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "archive-sample.csv"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ImportReport> ImportAsync(TestApp app, string text, ImportOptions? options = null)
    {
        await using var scope = app.Scope();
        return await scope.Get<CsvImportService>().ImportAsync(text, options ?? new ImportOptions(), Ct);
    }

    private static async Task<IReadOnlyList<Transaction>> AllTransactionsAsync(TestApp app)
    {
        await using var scope = app.Scope();
        return await scope.Get<ITransactionRepository>().ListAsync(cancellationToken: Ct);
    }

    [Fact]
    public async Task Archive_ImportsAll_MonthTotalsMatchFile()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var report = await ImportAsync(app, ArchiveText);

        report.Succeeded.ShouldBeTrue();
        report.Added.ShouldBe(6);
        report.SkippedDuplicates.ShouldBe(0);
        var stored = await AllTransactionsAsync(app);
        await using (var scope = app.Scope())
        {
            var accounts = (await scope.Get<IAccountRepository>().ListAsync(Ct)).ToDictionary(a => a.Id);
            SummaryCalculator.ByMonth(stored, accounts).ShouldBe(report.FileTotals);
        }
        report.FileTotals[new YearMonth(2025, 10)].Balance.ShouldBe(35006m + 2485.56m - 56638m - 981m - 839m);
    }

    [Fact]
    public async Task Archive_EmptyAccount_GoesToDefaultAccount_KnownCategoriesReused()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var report = await ImportAsync(app, ArchiveText, new ImportOptions(DefaultAccountName: "Наличные"));

        report.CreatedCategories.ShouldBeEmpty(); // «Работа» is in the starter catalog
        report.CreatedAccounts.ShouldBe(["Копилка (RUB)"]); // the archive's saving row becomes an envelope
        await using var scope = app.Scope();
        var cash = (await scope.Get<IAccountRepository>().ListAsync(Ct)).Single(a => a.Name == "Наличные");
        (await AllTransactionsAsync(app)).ShouldAllBe(t => t.AccountId == cash.Id);
        var transfer = (await AllTransactionsAsync(app)).Single(t => t.IsTransfer);
        transfer.Amount.ShouldBe(56638m);
        (await scope.Get<IAccountRepository>().FindAsync(transfer.ToAccountId!.Value, Ct))!.IsEnvelope.ShouldBeTrue();
    }

    [Fact]
    public async Task ImportingTwice_AddsNothing()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        await ImportAsync(app, ArchiveText);

        var second = await ImportAsync(app, ArchiveText);

        second.Added.ShouldBe(0);
        second.SkippedDuplicates.ShouldBe(6);
        (await AllTransactionsAsync(app)).Count.ShouldBe(6);
    }

    [Fact]
    public async Task IdenticalRowsInFile_AreKept_AndDeduplicatedByCount()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        const string twoCoffees = "date,type,amount,category,account,note\n2026-10-07,expense,350,Еда,Карта,кофе\n2026-10-07,expense,350,Еда,Карта,кофе\n";
        const string threeCoffees = twoCoffees + "2026-10-07,expense,350,Еда,Карта,кофе\n";

        (await ImportAsync(app, twoCoffees)).Added.ShouldBe(2);
        var report = await ImportAsync(app, threeCoffees);

        report.Added.ShouldBe(1);
        report.SkippedDuplicates.ShouldBe(2);
        (await AllTransactionsAsync(app)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task UnknownCategoryAndAccount_AreCreated()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var report = await ImportAsync(app, "date,type,amount,category,account\n2026-10-07,expense,100,Хобби,Тинькофф\n2026-10-08,saving,50,,\n");

        report.CreatedCategories.ShouldBe(["Хобби (Expense)"]);
        report.CreatedAccounts.ShouldBe(["Тинькофф (RUB)", "Копилка (RUB)"]);
    }

    [Fact]
    public async Task BadRow_CancelsWholeImport()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var report = await ImportAsync(app, ArchiveText + "2025-13-01,expense,1,Еда,,\n");

        report.Succeeded.ShouldBeFalse();
        report.Errors.ShouldHaveSingleItem().LineNumber.ShouldBe(8);
        (await AllTransactionsAsync(app)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DryRun_ReportsButChangesNothing()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var report = await ImportAsync(app, ArchiveText + "2026-01-01,expense,1,Новая категория,,\n", new ImportOptions(DryRun: true));

        report.Added.ShouldBe(7);
        report.CreatedCategories.ShouldBe(["Новая категория (Expense)"]);
        (await AllTransactionsAsync(app)).ShouldBeEmpty();
        await using var scope = app.Scope();
        (await scope.Get<ICategoryRepository>().ListAsync(Ct)).ShouldNotContain(c => c.Name == "Новая категория");
    }

    [Fact]
    public async Task ForeignCurrencyRow_RestoresAmountAndRate_FromOwnExport()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var id = Guid.NewGuid();
        var text = $"date,type,amount,category,account,note,currency,original_amount,id\n2026-10-07,expense,1849.13,Еда,Карта USD,ужин,USD,20,{id}\n";

        var report = await ImportAsync(app, text);

        report.CreatedAccounts.ShouldBe(["Карта USD (USD)"]);
        var transaction = (await AllTransactionsAsync(app)).ShouldHaveSingleItem();
        transaction.Id.ShouldBe(id);
        transaction.Amount.ShouldBe(20m);
        transaction.Currency.ShouldBe(CurrencyCode.Usd);
        transaction.BaseAmount.ShouldBe(1849.13m);
    }

    [Fact]
    public async Task ForeignCurrencyWithoutOriginalAmount_IsAnError()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var report = await ImportAsync(app, "date,type,amount,category,account,currency\n2026-10-07,expense,1849.13,Еда,Карта USD,USD\n");

        report.Errors.ShouldHaveSingleItem().Message.ShouldContain("original_amount");
    }

    [Fact]
    public async Task CurrencyMismatchWithExistingAccount_IsAnError()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var report = await ImportAsync(app, "date,type,amount,category,account,currency,original_amount\n2026-10-07,expense,1849.13,Еда,Карта,USD,20\n");

        report.Errors.ShouldHaveSingleItem().Message.ShouldContain("валюта");
    }

    [Fact]
    public async Task RowWithKnownId_UpdatesExistingTransaction()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var id = Guid.NewGuid();
        await ImportAsync(app, $"date,type,amount,category,account,note,id\n2026-10-07,expense,350,Еда,Карта,кофе,{id}\n");

        var report = await ImportAsync(app, $"date,type,amount,category,account,note,id\n2026-10-07,expense,400,Еда,Карта,капучино,{id}\n");

        report.Updated.ShouldBe(1);
        var transaction = (await AllTransactionsAsync(app)).ShouldHaveSingleItem();
        transaction.Amount.ShouldBe(400m);
        transaction.Note.ShouldBe("капучино");
    }

    [Fact]
    public async Task ExportThenImportIntoEmptyApp_RoundTripsEverything()
    {
        await using var source = await TestApp.CreateAsync(exportEnabled: false);
        await ImportAsync(source, ArchiveText);
        await ImportAsync(source, "date,type,amount,category,account,note,currency,original_amount\n2026-10-07,expense,1849.13,Еда,Карта USD,\"ужин, \"\"Ромашка\"\"\",USD,20\n");
        string exported;
        await using (var scope = source.Scope())
        {
            var path = Path.Combine(source.Paths.DataDirectory, "export.csv");
            await scope.Get<CsvExportService>().ExportAsync(path, Ct);
            exported = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, Ct));
        }

        await using var target = await TestApp.CreateAsync(exportEnabled: false);
        var report = await ImportAsync(target, exported);

        report.Succeeded.ShouldBeTrue();
        report.Added.ShouldBe(7);
        var original = await AllTransactionsAsync(source);
        var copy = await AllTransactionsAsync(target);
        copy.Select(t => (t.Id, t.Date, t.Type, t.Amount, t.Currency, t.BaseAmount, t.Note))
            .ShouldBe(original.Select(t => (t.Id, t.Date, t.Type, t.Amount, t.Currency, t.BaseAmount, t.Note)), ignoreOrder: true);
        TransactionCsvReader.ReadText(exported).Rows.Count.ShouldBe(7);
    }

    [Fact]
    public async Task ExportV2_WithAccountsFile_RoundTripsEnvelopesAndTransfers()
    {
        await using var source = await TestApp.CreateAsync(exportEnabled: false);
        Account card;
        Account car;
        await using (var scope = source.Scope())
        {
            card = (await scope.Get<IAccountRepository>().ListAsync(Ct)).Single(a => a.Name == "Карта");
            car = await scope.Get<FinanceApp.Application.Accounts.AccountService>().CreateAsync(
                new FinanceApp.Application.Accounts.AccountInput("Машина", AccountKind.Envelope, GoalAmount: 150_000m, GoalDate: new DateOnly(2027, 5, 1)), Ct);
            await scope.Get<FinanceApp.Application.Transactions.TransactionService>().AddTransferAsync(
                new FinanceApp.Application.Transactions.TransferInput(new DateOnly(2026, 10, 15), 10_000m, card.Id, car.Id, "в конверт"), Ct);
        }

        var folder = Path.Combine(source.Paths.DataDirectory, "export");
        await using (var scope = source.Scope())
        {
            await scope.Get<CsvExportService>().ExportAsync(Path.Combine(folder, "transactions.csv"), Ct);
        }

        var transactionsText = await File.ReadAllTextAsync(Path.Combine(folder, "transactions.csv"), Ct);
        var accountsText = await File.ReadAllTextAsync(Path.Combine(folder, "accounts.csv"), Ct);
        accountsText.ShouldContain("Машина,envelope,RUB,10000,false,150000,2027-05-01");
        accountsText.ShouldContain("Карта,account,RUB,-10000,false,,");

        await using var target = await TestApp.CreateAsync(exportEnabled: false);
        var report = await ImportAsync(target, transactionsText, new ImportOptions(AccountsCsvText: accountsText));

        report.Succeeded.ShouldBeTrue();
        await using var check = target.Scope();
        var restored = (await check.Get<IAccountRepository>().ListAsync(Ct)).Single(a => a.Name == "Машина");
        restored.IsEnvelope.ShouldBeTrue();
        restored.GoalAmount.ShouldBe(150_000m);
        var transfer = (await AllTransactionsAsync(target)).ShouldHaveSingleItem();
        transfer.ToAccountId.ShouldBe(restored.Id);
        transfer.Note.ShouldBe("в конверт");
        report.FileTotals[new YearMonth(2026, 10)].IntoEnvelopes.ShouldBe(10_000m);
    }

    [Fact]
    public async Task SuccessfulImport_TriggersCsvExport()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: true);

        await ImportAsync(app, ArchiveText);
        await app.Export.FlushAsync(Ct);

        app.Export.Status.State.ShouldBe(CsvExportState.Succeeded);
        app.Export.Status.Rows.ShouldBe(6);
    }
}
