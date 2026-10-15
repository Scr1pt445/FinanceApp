using FinanceApp.Application.Abstractions;
using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests.QuickEntry;

/// <summary>Quick entry through the real service graph and a real SQLite database.</summary>
public class QuickEntryServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SavedTransaction> SaveAsync(TestApp app, string line)
    {
        await using var scope = app.Scope();
        return await scope.Get<QuickEntryService>().SaveAsync(line, Ct);
    }

    private static async Task<QuickEntryDraft> PreviewAsync(TestApp app, string line)
    {
        await using var scope = app.Scope();
        return QuickEntryService.Preview(line, await scope.Get<QuickEntryService>().LoadContextAsync(Ct));
    }

    [Fact]
    public async Task Save_StoresTransaction()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var saved = await SaveAsync(app, "350,50 еда кофе вчера");

        var t = saved.Transaction;
        t.Type.ShouldBe(TransactionType.Expense);
        t.Amount.ShouldBe(350.50m);
        t.Note.ShouldBe("кофе");
        t.Date.ShouldBe(DateOnly.FromDateTime(DateTime.Now).AddDays(-1));
        await using var scope = app.Scope();
        (await scope.Get<ITransactionRepository>().ListAsync(cancellationToken: Ct)).ShouldHaveSingleItem().Id.ShouldBe(t.Id);
    }

    [Fact]
    public async Task NextEntry_LearnsCategoryFromNotes_AndRemembersAccount()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        await SaveAsync(app, "350 еда кофе @нал");

        var draft = await PreviewAsync(app, "200 кофе");

        draft.Category.ShouldNotBeNull().Name.ShouldBe("Еда");
        draft.CategoryFromHint.ShouldBeTrue();
        draft.Account.ShouldNotBeNull().Name.ShouldBe("Наличные");
        draft.AccountIsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task DefaultAccount_IsPerType_AndNeverForeign()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        await using (var scope = app.Scope())
        {
            scope.Get<IAccountRepository>().Add(new Account(Guid.NewGuid(), "Карта USD", CurrencyCode.Usd, 10));
            await scope.Get<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await SaveAsync(app, "350 еда @нал");
        await SaveAsync(app, ">10000 копилка @вклад");
        await SaveAsync(app, "20 еда ужин @карта usd");

        (await PreviewAsync(app, "1200 тр такси")).Account.ShouldNotBeNull().Name.ShouldBe("Наличные");
        (await PreviewAsync(app, ">500 копилка")).Account.ShouldNotBeNull().Name.ShouldBe("Вклад");
    }

    [Fact]
    public async Task IncompleteLine_IsRejected_NothingSaved()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var error = await Should.ThrowAsync<QuickEntryException>(() => SaveAsync(app, "350 непонятно что"));

        error.Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.MissingCategory);
        await using var scope = app.Scope();
        (await scope.Get<ITransactionRepository>().ListAsync(cancellationToken: Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ForeignCurrencyAccount_UsesRateOfTheDay()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        app.Rates.Usd = 92.5m;
        await using (var scope = app.Scope())
        {
            scope.Get<IAccountRepository>().Add(new Account(Guid.NewGuid(), "Карта USD", CurrencyCode.Usd, 10));
            await scope.Get<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        var saved = await SaveAsync(app, "20 еда ужин @карта usd");

        saved.Transaction.Currency.ShouldBe(CurrencyCode.Usd);
        saved.Transaction.Amount.ShouldBe(20m);
        saved.Transaction.BaseAmount.ShouldBe(1850m);
    }
}
