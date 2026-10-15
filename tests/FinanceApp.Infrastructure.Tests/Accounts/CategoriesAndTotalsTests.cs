using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Accounts;
using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests.Accounts;

public class CategoriesAndTotalsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateCategory_IsOfferedInTheCatalog()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        await using (var scope = app.Scope())
        {
            await scope.Get<CategoryService>().CreateAsync(" Хобби ", TransactionType.Expense, Ct);
        }

        await using var check = app.Scope();
        var catalog = await check.Get<CatalogService>().LoadAsync(Ct);
        catalog.CategoriesFor(TransactionType.Expense).ShouldContain(c => c.Name == "Хобби");
        catalog.CategoriesFor(TransactionType.Income).ShouldNotContain(c => c.Name == "Хобби");
    }

    [Fact]
    public async Task SameNameInSameType_IsRejected_ButAllowedInTheOtherType()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        await using var scope = app.Scope();
        var service = scope.Get<CategoryService>();

        await Should.ThrowAsync<DomainException>(() => service.CreateAsync("еда", TransactionType.Expense, Ct));
        (await service.CreateAsync("Еда", TransactionType.Income, Ct)).Type.ShouldBe(TransactionType.Income);
    }

    [Fact]
    public async Task CreatingAHiddenCategoryAgain_ShowsIt()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        await using var scope = app.Scope();
        var service = scope.Get<CategoryService>();
        var food = (await scope.Get<ICategoryRepository>().ListAsync(Ct)).Single(c => c.Name == "Еда");
        await service.SetHiddenAsync(food.Id, true, Ct);

        var again = await service.CreateAsync("Еда", TransactionType.Expense, Ct);

        again.Id.ShouldBe(food.Id);
        again.IsHidden.ShouldBeFalse();
    }

    [Fact]
    public async Task Rename_KeepsHistory_AndChecksDuplicates()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        await using (var scope = app.Scope())
        {
            await scope.Get<QuickEntryService>().SaveAsync("350 еда кофе", Ct);
        }

        await using var check = app.Scope();
        var service = check.Get<CategoryService>();
        var food = (await check.Get<ICategoryRepository>().ListAsync(Ct)).Single(c => c.Name == "Еда");

        await service.RenameAsync(food.Id, "Продукты", Ct);
        await Should.ThrowAsync<DomainException>(() => service.RenameAsync(food.Id, "транспорт", Ct));

        var transaction = (await check.Get<ITransactionRepository>().ListAsync(cancellationToken: Ct)).ShouldHaveSingleItem();
        transaction.CategoryId.ShouldBe(food.Id);
        (await check.Get<ICategoryRepository>().FindAsync(food.Id, Ct))!.Name.ShouldBe("Продукты");
    }

    [Fact]
    public async Task Totals_InAnotherCurrency()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        app.Rates.Usd = 90m;
        await using (var scope = app.Scope())
        {
            await scope.Get<QuickEntryService>().SaveAsync("+9000 зарплата", Ct);
        }

        var store = app.Get<ISettingsStore>();
        await store.SaveAsync(store.Load() with { TotalsCurrency = CurrencyCode.Usd }, Ct);

        await using var check = app.Scope();
        var balances = await check.Get<AccountBalancesService>().LoadAsync(cancellationToken: Ct);

        balances.DisplayCurrency.ShouldBe(CurrencyCode.Usd);
        balances.Total.ShouldBe(9000m);
        balances.InDisplay(balances.Total).ShouldBe(100m);
        balances.DisplayChoices.ShouldBe([CurrencyCode.Rub, CurrencyCode.Usd, CurrencyCode.Eur]);
    }
}
