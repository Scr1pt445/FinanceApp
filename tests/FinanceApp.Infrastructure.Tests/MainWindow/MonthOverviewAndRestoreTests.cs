using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Export;
using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests.MainWindow;

public class MonthOverviewAndRestoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Transaction> QuickAsync(TestApp app, string line)
    {
        await using var scope = app.Scope();
        return (await scope.Get<QuickEntryService>().SaveAsync(line, Ct)).Transaction;
    }

    [Fact]
    public async Task Overview_ContainsMonthNewestFirst_AndComparesWithPreviousMonth()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        await QuickAsync(app, "100 еда завтрак 01.10.2026");
        await QuickAsync(app, "300 еда ужин 20.10.2026");
        await QuickAsync(app, "+1000 зарплата 15.10.2026");
        await QuickAsync(app, "50 еда 30.09.2026");
        await QuickAsync(app, "999 еда 01.11.2026");

        await using var scope = app.Scope();
        var overview = await scope.Get<PeriodOverviewService>().LoadAsync(Period.Of(new YearMonth(2026, 10)), Ct);

        overview.Transactions.Select(t => t.Note ?? t.Type.ToString()).ShouldBe(["ужин", "Income", "завтрак"]);
        overview.Summary.Current.ShouldBe(new PeriodTotals(1000m, 400m, 0m));
        overview.Summary.Previous!.Expense.ShouldBe(50m);
        overview.Categories.ShouldNotBeEmpty();
        overview.Accounts.ShouldNotBeEmpty();
        overview.BaseCurrency.ShouldBe(CurrencyCode.Rub);
    }

    [Fact]
    public async Task Restore_PutsDeletedTransactionBack_Unchanged_AndExportsIt()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: true);
        var original = await QuickAsync(app, "350 еда кофе");

        Transaction deleted;
        await using (var scope = app.Scope())
        {
            deleted = await scope.Get<TransactionService>().DeleteAsync(original.Id, Ct);
        }

        await using (var scope = app.Scope())
        {
            await scope.Get<TransactionService>().RestoreAsync(deleted, Ct);
        }

        await using (var scope = app.Scope())
        {
            var restored = (await scope.Get<ITransactionRepository>().ListAsync(cancellationToken: Ct)).ShouldHaveSingleItem();
            restored.Id.ShouldBe(original.Id);
            restored.CreatedAt.ShouldBe(original.CreatedAt);
            restored.Amount.ShouldBe(350m);
            restored.Note.ShouldBe("кофе");
        }

        await app.Export.FlushAsync(Ct);
        app.Export.Status.State.ShouldBe(CsvExportState.Succeeded);
        app.Export.Status.Rows.ShouldBe(1);
    }

    [Fact]
    public async Task Restore_Twice_IsHarmless()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var original = await QuickAsync(app, "350 еда кофе");
        Transaction deleted;
        await using (var scope = app.Scope())
        {
            deleted = await scope.Get<TransactionService>().DeleteAsync(original.Id, Ct);
        }

        for (var i = 0; i < 2; i++)
        {
            await using var scope = app.Scope();
            await scope.Get<TransactionService>().RestoreAsync(deleted, Ct);
        }

        await using var check = app.Scope();
        (await check.Get<ITransactionRepository>().ListAsync(cancellationToken: Ct)).Count.ShouldBe(1);
    }
}
