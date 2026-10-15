using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Core.Tests.Presentation;

public class OwnCategoriesInTheFormTests : FinanceViewModelTestBase
{
    private async Task<TransactionFormViewModel> NewAsync(TransactionType kind, Account? account = null, Account? toAccount = null) =>
        TransactionFormViewModel.ForNew(Backend, await Backend.LoadCatalogAsync(Ct), FakeFinanceBackend.Today, kind, account, toAccount);

    [Fact]
    public async Task AddCategory_CreatesItOfTheCurrentKind_AndSelectsIt()
    {
        var form = await NewAsync(TransactionType.Income);
        form.StartAddCategoryCommand.Execute(null);
        form.IsAddingCategory.ShouldBeTrue();

        form.NewCategoryName = "Кэшбэк";
        await form.AddCategoryCommand.ExecuteAsync(null);

        form.IsAddingCategory.ShouldBeFalse();
        Backend.Categories.ShouldContain(c => c.Name == "Кэшбэк" && c.Type == TransactionType.Income);
        form.Category!.Name.ShouldBe("Кэшбэк");
        form.Tiles.Single(t => t.IsSelected).Name.ShouldBe("Кэшбэк");
    }

    [Fact]
    public async Task AddedCategory_SurvivesSwitchingKinds()
    {
        var form = await NewAsync(TransactionType.Expense);
        form.NewCategoryName = "Хобби";
        await form.AddCategoryCommand.ExecuteAsync(null);

        form.ChooseKindCommand.Execute(TransactionType.Income);
        form.Tiles.ShouldNotContain(t => t.Name == "Хобби");
        form.ChooseKindCommand.Execute(TransactionType.Expense);

        form.Tiles.ShouldContain(t => t.Name == "Хобби");
    }

    [Fact]
    public async Task DuplicateCategory_IsShown_AndInputStaysOpen()
    {
        var form = await NewAsync(TransactionType.Expense);
        form.StartAddCategoryCommand.Execute(null);
        form.NewCategoryName = "еда";

        await form.AddCategoryCommand.ExecuteAsync(null);

        form.IsAddingCategory.ShouldBeTrue();
        form.SaveError.ShouldContain("уже есть");
    }
}

public class TransferByRateTests : FinanceViewModelTestBase
{
    private Account Usd { get; }

    public TransferByRateTests()
    {
        Usd = Backend.Add(new Account(Guid.NewGuid(), "Наличные USD", CurrencyCode.Usd));
    }

    private async Task<TransactionFormViewModel> TransferAsync(Account from, Account to)
    {
        var form = TransactionFormViewModel.ForNew(Backend, await Backend.LoadCatalogAsync(Ct), FakeFinanceBackend.Today, TransactionType.Transfer, from, to);
        form.UseRate = true;
        return form;
    }

    [Fact]
    public async Task FromBaseCurrency_RateIsQuotedForTheOtherOne_AndDivides()
    {
        var form = await TransferAsync(Backend.Card, Usd);
        form.AmountText = "9 000";
        form.RateText = "90";

        form.RateLabel.ShouldBe("1 USD =");
        form.RateUnit.ShouldBe("RUB");
        form.ReceivedPreview.ShouldBe("Зачислится 100 $");
        await form.SaveCommand.ExecuteAsync(null);

        Backend.Transactions.ShouldHaveSingleItem().ToAmount.ShouldBe(100m);
    }

    [Fact]
    public async Task IntoBaseCurrency_Multiplies()
    {
        var form = await TransferAsync(Usd, Backend.Card);
        form.AmountText = "100";
        form.RateText = "92,5";

        form.RateLabel.ShouldBe("1 USD =");
        await form.SaveCommand.ExecuteAsync(null);

        Backend.Transactions.ShouldHaveSingleItem().ToAmount.ShouldBe(9_250m);
    }

    [Fact]
    public async Task SwapRate_InvertsTheQuote_KeepingTheResult()
    {
        var form = await TransferAsync(Backend.Card, Usd);
        form.AmountText = "9000";
        form.RateText = "90";

        form.SwapRateCommand.Execute(null);

        form.RateLabel.ShouldBe("1 RUB =");
        form.RateText.ShouldBe("0,011111");
        form.ReceivedPreview.ShouldBe("Зачислится 100 $");
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("курс")]
    public async Task InvalidRate_BlocksSave(string rate)
    {
        var form = await TransferAsync(Backend.Card, Usd);
        form.AmountText = "9000";

        form.RateText = rate;

        form.Problem.ShouldBe("Курс — положительное число.");
        form.CanSave.ShouldBeFalse();
    }

    [Fact]
    public async Task ReceivedMode_StillWorks_AndEmptyMeansCbrRate()
    {
        var form = await TransferAsync(Backend.Card, Usd);
        form.UseRate = false;
        form.AmountText = "9000";

        form.CanSave.ShouldBeTrue();
        form.ReceivedPreview.ShouldBeEmpty();
    }
}

public class CategoriesViewModelTests : FinanceViewModelTestBase
{
    private async Task<CategoriesViewModel> OpenAsync()
    {
        var viewModel = new CategoriesViewModel(Backend);
        await viewModel.LoadAsync(Ct);
        return viewModel;
    }

    [Fact]
    public async Task Lists_ExpensesAndIncomes_MostUsedFirst()
    {
        Backend.Spend("2026-10-01", 1m, "Транспорт");
        Backend.Spend("2026-10-02", 1m, "Транспорт");

        var viewModel = await OpenAsync();

        viewModel.Expenses[0].Name.ShouldBe("Транспорт");
        viewModel.Expenses[0].UsageText.ShouldBe("операций: 2");
        viewModel.Incomes.ShouldContain(c => c.Name == "Зарплата");
        viewModel.Expenses.ShouldNotContain(c => c.Name == "Зарплата");
    }

    [Fact]
    public async Task Add_Rename_Hide()
    {
        var viewModel = await OpenAsync();

        viewModel.NewExpenseName = "Хобби";
        await viewModel.AddExpenseCommand.ExecuteAsync(null);
        viewModel.NewExpenseName.ShouldBeEmpty();
        var hobby = viewModel.Expenses.Single(c => c.Name == "Хобби");

        hobby.StartRenameCommand.Execute(null);
        hobby.Name = "Увлечения";
        await hobby.FinishRenameCommand.ExecuteAsync(null);
        Backend.Named("Увлечения").ShouldNotBeNull();

        await viewModel.Expenses.Single(c => c.Name == "Увлечения").ToggleHiddenCommand.ExecuteAsync(null);
        viewModel.Expenses.ShouldNotContain(c => c.Name == "Увлечения");

        viewModel.ShowHidden = true;
        await viewModel.LoadAsync(Ct);
        viewModel.Expenses.Last().IsHidden.ShouldBeTrue();
    }

    [Fact]
    public async Task Duplicate_IsShownAsStatus()
    {
        var viewModel = await OpenAsync();
        viewModel.NewIncomeName = "зарплата";

        await viewModel.AddIncomeCommand.ExecuteAsync(null);

        viewModel.Status.ShouldContain("уже есть");
    }

    [Fact]
    public async Task CancelRename_RestoresTheName()
    {
        var viewModel = await OpenAsync();
        var food = viewModel.Expenses.Single(c => c.Name == "Еда");

        food.StartRenameCommand.Execute(null);
        food.Name = "Продукты";
        food.CancelRenameCommand.Execute(null);

        food.Name.ShouldBe("Еда");
        food.IsEditing.ShouldBeFalse();
        Backend.Named("Еда").Name.ShouldBe("Еда");
    }
}

public class TotalsCurrencyTests : FinanceViewModelTestBase
{
    [Fact]
    public async Task ChoosingACurrency_ShowsTotalsInIt_AndIsRemembered()
    {
        Backend.Earn("2026-10-01", 90_000m);
        Backend.Move("2026-10-02", 54_000m, Backend.Card, Car);
        var shell = new ShellViewModel(Backend, Time);
        await shell.RefreshAsync(Ct);
        shell.TotalsCurrencies.ShouldBe([CurrencyCode.Rub, CurrencyCode.Usd, CurrencyCode.Eur]);
        shell.TotalsCurrency.ShouldBe(CurrencyCode.Rub);

        shell.TotalsCurrency = CurrencyCode.Usd;
        await shell.RefreshAsync(Ct);

        Backend.TotalsCurrency.ShouldBe(CurrencyCode.Usd);
        shell.TotalText.ShouldBe("1 000 $");
        shell.AccountsTotalText.ShouldBe("400 $");
        shell.EnvelopesTotalText.ShouldBe("600 $");
    }

    [Fact]
    public async Task EnvelopePage_And_CategoriesPage()
    {
        var shell = new ShellViewModel(Backend, Time);
        await shell.RefreshAsync(Ct);

        shell.Selected = shell.Envelopes.Single();
        shell.IsEnvelopeSelected.ShouldBeTrue();
        shell.IsOperations.ShouldBeTrue();

        shell.Selected = shell.CategoriesItem;
        shell.IsCategories.ShouldBeTrue();
        shell.IsOperations.ShouldBeFalse();
        await shell.RefreshAsync(Ct);
        shell.Categories.Expenses.ShouldNotBeEmpty();
    }
}
