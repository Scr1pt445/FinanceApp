using System.Text;
using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Csv;
using FinanceApp.Application.Export;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests;

/// <summary>End to end: use case → SQLite → background CSV export, through the real DI graph.</summary>
public class TransactionFlowTests
{
    private static readonly DateOnly Oct7 = new(2026, 10, 7);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Category Category, Account Account)> FindAsync(TestApp app, string category, string account)
    {
        await using var scope = app.Scope();
        var categories = await scope.Get<ICategoryRepository>().ListAsync(Ct);
        var accounts = await scope.Get<IAccountRepository>().ListAsync(Ct);
        return (categories.First(c => c.Name == category), accounts.First(a => a.Name == account));
    }

    private static async Task<SavedTransaction> AddAsync(TestApp app, TransactionInput input)
    {
        await using var scope = app.Scope();
        return await scope.Get<TransactionService>().AddAsync(input, Ct);
    }

    private static async Task<string> WaitForCsvAsync(TestApp app, Func<string, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var text = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(app.CsvPath, Ct));
                if (condition(text))
                {
                    return text;
                }
            }
            catch (IOException)
            {
                // Not created yet, or being swapped by the exporter right now — like Obsidian would see it.
            }

            await Task.Delay(25, Ct);
        }

        throw new TimeoutException("CSV was not updated in time.");
    }

    [Fact]
    public async Task Add_Update_Delete_AreReflectedInCsv()
    {
        await using var app = await TestApp.CreateAsync();
        var (food, card) = await FindAsync(app, "Еда", "Карта");

        var saved = await AddAsync(app, new TransactionInput(Oct7, TransactionType.Expense, 350m, food.Id, card.Id, "кофе, круассан"));
        var csv = await WaitForCsvAsync(app, text => text.Contains(saved.Transaction.Id.ToString(), StringComparison.Ordinal));
        csv.Split('\n')[1].ShouldBe($"2026-10-07,expense,350,Еда,Карта,\"кофе, круассан\",RUB,350,{saved.Transaction.Id},,");

        await using (var scope = app.Scope())
        {
            await scope.Get<TransactionService>().UpdateAsync(
                saved.Transaction.Id, new TransactionInput(Oct7, TransactionType.Expense, 400.5m, food.Id, card.Id, "кофе"), Ct);
        }

        csv = await WaitForCsvAsync(app, text => text.Contains(",400.50,", StringComparison.Ordinal));
        csv.Split('\n')[1].ShouldStartWith("2026-10-07,expense,400.50,Еда,Карта,кофе,");

        await using (var scope = app.Scope())
        {
            await scope.Get<TransactionService>().DeleteAsync(saved.Transaction.Id, Ct);
        }

        csv = await WaitForCsvAsync(app, text => !text.Contains(saved.Transaction.Id.ToString(), StringComparison.Ordinal));
        csv.ShouldBe(string.Join(',', TransactionCsvFormat.Columns) + "\n");
    }

    [Fact]
    public async Task ForeignCurrency_UsesRateOfTheDay_AndExportsBaseAmount()
    {
        await using var app = await TestApp.CreateAsync();
        app.Rates.Usd = 92.4567m;
        var (food, _) = await FindAsync(app, "Еда", "Карта");
        Guid usdAccountId;
        await using (var scope = app.Scope())
        {
            var account = new Account(Guid.NewGuid(), "Карта USD", CurrencyCode.Usd);
            scope.Get<IAccountRepository>().Add(account);
            await scope.Get<IUnitOfWork>().SaveChangesAsync(Ct);
            usdAccountId = account.Id;
        }

        var saved = await AddAsync(app, new TransactionInput(Oct7, TransactionType.Expense, 20m, food.Id, usdAccountId, "ужин"));

        saved.Transaction.ExchangeRate.ShouldBe(92.4567m);
        saved.Transaction.BaseAmount.ShouldBe(1849.13m);
        saved.RateIsFallback.ShouldBeFalse();
        await app.Export.FlushAsync(Ct);
        var csv = await WaitForCsvAsync(app, text => text.Contains("ужин", StringComparison.Ordinal));
        csv.Split('\n')[1].ShouldStartWith("2026-10-07,expense,1849.13,Еда,Карта USD,ужин,USD,20,");
    }

    [Fact]
    public async Task Update_KeepsManualRate_WhenDateAndAccountUnchanged()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var (food, _) = await FindAsync(app, "Еда", "Карта");
        Guid usdAccountId;
        await using (var scope = app.Scope())
        {
            var account = new Account(Guid.NewGuid(), "Наличные USD", CurrencyCode.Usd);
            scope.Get<IAccountRepository>().Add(account);
            await scope.Get<IUnitOfWork>().SaveChangesAsync(Ct);
            usdAccountId = account.Id;
        }

        var saved = await AddAsync(app, new TransactionInput(Oct7, TransactionType.Expense, 20m, food.Id, usdAccountId, null, ExchangeRate: 100m));
        await using (var scope = app.Scope())
        {
            var updated = await scope.Get<TransactionService>().UpdateAsync(
                saved.Transaction.Id, new TransactionInput(Oct7, TransactionType.Expense, 30m, food.Id, usdAccountId, "поправил"), Ct);
            updated.Transaction.ExchangeRate.ShouldBe(100m);
        }

        app.Rates.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ExportDisabled_WritesNoFile()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var (food, card) = await FindAsync(app, "Еда", "Карта");

        await AddAsync(app, new TransactionInput(Oct7, TransactionType.Expense, 350m, food.Id, card.Id, null));
        await app.Export.FlushAsync(Ct);
        await Task.Delay(100, Ct);

        File.Exists(app.CsvPath).ShouldBeFalse();
        app.Export.Status.State.ShouldBe(CsvExportState.Idle);
    }

    [Fact]
    public async Task UnknownCategory_IsRejected_NothingSaved()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var (_, card) = await FindAsync(app, "Еда", "Карта");

        await Should.ThrowAsync<DomainException>(
            () => AddAsync(app, new TransactionInput(Oct7, TransactionType.Expense, 350m, Guid.NewGuid(), card.Id, null)));

        await using var scope = app.Scope();
        (await scope.Get<ITransactionRepository>().ListAsync(cancellationToken: Ct)).ShouldBeEmpty();
    }
}
