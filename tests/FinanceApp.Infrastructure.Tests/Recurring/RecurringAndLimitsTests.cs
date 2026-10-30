using FinanceApp.Application.Accounts;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests.Recurring;

/// <summary>Recurring operations and monthly limits on a real database, migrations included.</summary>
public sealed class RecurringAndLimitsTests : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private TestApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await TestApp.CreateAsync(exportEnabled: false);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private async Task<Catalog> CatalogAsync()
    {
        await using var scope = _app.Scope();
        return await scope.Get<CatalogService>().LoadAsync(Ct);
    }

    [Fact]
    public async Task Rent_IsDueOnItsDay_RecordedOnce_ThenMovesToNextMonth()
    {
        var catalog = await CatalogAsync();
        var housing = catalog.Categories.First(c => c.Type == TransactionType.Expense);
        var card = catalog.DefaultAccount(TransactionType.Expense)!;
        Guid id;
        await using (var scope = _app.Scope())
        {
            var added = await scope.Get<RecurringService>().AddAsync(
                new RecurringInput(TransactionType.Expense, 30_000m, housing.Id, card.Id, null, "Аренда", RecurrenceFrequency.Monthly, Today), Ct);
            id = added.Id;
        }

        await using (var scope = _app.Scope())
        {
            var service = scope.Get<RecurringService>();
            (await service.DueAsync(Ct)).ShouldHaveSingleItem().Note.ShouldBe("Аренда");

            var saved = await service.RecordAsync(id, 31_500m, Ct);

            saved.Transaction.Amount.ShouldBe(31_500m);
            saved.Transaction.Date.ShouldBe(Today);
            saved.Transaction.Note.ShouldBe("Аренда");
        }

        await using (var scope = _app.Scope())
        {
            var service = scope.Get<RecurringService>();
            (await service.DueAsync(Ct)).ShouldBeEmpty();
            var next = (await service.ListAsync(Ct)).ShouldHaveSingleItem();
            next.NextDate.ShouldBe(RecurringOperation.After(Today, RecurrenceFrequency.Monthly, Today.Day));
            next.Amount.ShouldBe(30_000m); // a different bill once does not change the usual amount
        }
    }

    [Fact]
    public async Task Transfer_ToAnEnvelope_CanRecur_AndSkipping_RecordsNothing()
    {
        var catalog = await CatalogAsync();
        var card = catalog.DefaultAccount(TransactionType.Expense)!;
        RecurringOperation added;
        Account envelope;
        await using (var scope = _app.Scope())
        {
            envelope = await scope.Get<AccountService>().CreateAsync(new AccountInput("Отпуск", AccountKind.Envelope), Ct);
            added = await scope.Get<RecurringService>().AddAsync(
                new RecurringInput(TransactionType.Transfer, 5_000m, null, card.Id, envelope.Id, null, RecurrenceFrequency.Weekly, Today.AddDays(-1)), Ct);
        }

        await using (var scope = _app.Scope())
        {
            await scope.Get<RecurringService>().SkipAsync(added.Id, Ct);
        }

        await using (var scope = _app.Scope())
        {
            (await scope.Get<RecurringService>().ListAsync(Ct)).Single().NextDate.ShouldBe(Today.AddDays(6));
            (await scope.Get<AccountBalancesService>().LoadAsync(cancellationToken: Ct)).Envelopes.Single(e => e.Account.Id == envelope.Id).Balance.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Limits_CountTheMonthsExpenses_InTheBaseCurrency()
    {
        var catalog = await CatalogAsync();
        var food = catalog.Categories.Single(c => c.Name == "Еда" && c.Type == TransactionType.Expense);
        var card = catalog.DefaultAccount(TransactionType.Expense)!;
        await using (var scope = _app.Scope())
        {
            await scope.Get<CategoryService>().SetLimitAsync(food.Id, 10_000m, Ct);
            var transactions = scope.Get<TransactionService>();
            await transactions.AddAsync(new TransactionInput(Today, TransactionType.Expense, 6_000m, food.Id, card.Id, null), Ct);
            await transactions.AddAsync(new TransactionInput(Today, TransactionType.Expense, 2_500m, food.Id, card.Id, null), Ct);

            // Last month does not count.
            await transactions.AddAsync(new TransactionInput(new DateOnly(Today.Year, Today.Month, 1).AddDays(-1), TransactionType.Expense, 9_000m, food.Id, card.Id, null), Ct);
        }

        await using (var scope = _app.Scope())
        {
            var limits = await scope.Get<LimitsService>().LoadAsync(YearMonth.Of(Today), Ct);
            var line = limits.Lines.ShouldHaveSingleItem();
            line.Spent.ShouldBe(8_500m);
            line.Left.ShouldBe(1_500m);
            line.IsNear.ShouldBeTrue();
            line.IsOver.ShouldBeFalse();
        }

        await using (var scope = _app.Scope())
        {
            await scope.Get<CategoryService>().SetLimitAsync(food.Id, null, Ct);
            (await scope.Get<LimitsService>().LoadAsync(YearMonth.Of(Today), Ct)).Lines.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Limit_OnAnIncomeCategory_IsRefused()
    {
        var salary = (await CatalogAsync()).Categories.First(c => c.Type == TransactionType.Income);
        await using var scope = _app.Scope();

        await Should.ThrowAsync<DomainException>(() => scope.Get<CategoryService>().SetLimitAsync(salary.Id, 1000m, Ct));
    }
}
