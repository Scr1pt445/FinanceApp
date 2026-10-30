using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Setup;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Presentation.QuickEntry;

namespace FinanceApp.Core.Tests.Presentation;

public class QuickEntryViewModelTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly Account Cash = new(Guid.NewGuid(), "Наличные", CurrencyCode.Rub);
    private static readonly List<Category> Categories = DefaultCatalog.CreateCategories().ToList();

    private readonly FakeBackend _backend = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<QuickEntryViewModel> OpenAsync()
    {
        var viewModel = new QuickEntryViewModel(_backend);
        await viewModel.OpenAsync(Ct);
        return viewModel;
    }

    [Fact]
    public async Task EmptyLine_ShowsHint_CannotSave()
    {
        var viewModel = await OpenAsync();

        viewModel.Message.ShouldBe(QuickEntryViewModel.Hint);
        viewModel.HasProblem.ShouldBeFalse();
        viewModel.Headline.ShouldBeEmpty();
        viewModel.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Typing_UpdatesPreviewLive()
    {
        var viewModel = await OpenAsync();

        viewModel.Input = "350,5 еда кофе вчера";

        viewModel.Headline.ShouldBe("Расход 350,50 ₽");
        viewModel.Details.ShouldBe("Еда · Наличные · вчера · «кофе»");
        viewModel.HasProblem.ShouldBeFalse();
        viewModel.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task IncompleteLine_ShowsProblem_CannotSave()
    {
        var viewModel = await OpenAsync();

        viewModel.Input = "350 непонятно";

        viewModel.HasProblem.ShouldBeTrue();
        viewModel.Message.ShouldContain("категори");
        viewModel.Details.ShouldStartWith("категория?");
        viewModel.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Save_CallsBackend_ClearsLine_RaisesSaved()
    {
        var viewModel = await OpenAsync();
        SavedTransaction? saved = null;
        viewModel.Saved += (_, s) => saved = s;
        viewModel.Input = "350 еда кофе";

        await viewModel.SaveCommand.ExecuteAsync(null);

        _backend.SavedLines.ShouldBe(["350 еда кофе"]);
        saved.ShouldNotBeNull();
        viewModel.Input.ShouldBeEmpty();
        viewModel.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveFailure_KeepsLine_ShowsError()
    {
        _backend.SaveError = new InvalidOperationException("база занята");
        var viewModel = await OpenAsync();
        var savedRaised = false;
        viewModel.Saved += (_, _) => savedRaised = true;
        viewModel.Input = "350 еда кофе";

        await viewModel.SaveCommand.ExecuteAsync(null);

        savedRaised.ShouldBeFalse();
        viewModel.Input.ShouldBe("350 еда кофе");
        viewModel.HasProblem.ShouldBeTrue();
        viewModel.Message.ShouldContain("база занята");
    }

    [Fact]
    public async Task Open_ClearsPreviousLine()
    {
        var viewModel = await OpenAsync();
        viewModel.Input = "350 еда";

        await viewModel.OpenAsync(Ct);

        viewModel.Input.ShouldBeEmpty();
        viewModel.Headline.ShouldBeEmpty();
    }

    [Fact]
    public async Task ContextLoadFailure_IsShown()
    {
        _backend.LoadError = new IOException("нет доступа к базе");

        var viewModel = await OpenAsync();

        viewModel.HasProblem.ShouldBeTrue();
        viewModel.Message.ShouldContain("нет доступа к базе");
    }

    [Fact]
    public async Task Cancel_RequestsClose()
    {
        var viewModel = await OpenAsync();
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        viewModel.CancelCommand.Execute(null);

        closed.ShouldBeTrue();
    }

    [Fact]
    public void Formatter_Dates()
    {
        QuickEntryFormatter.Date(Today, Today).ShouldBe("сегодня");
        QuickEntryFormatter.Date(Today.AddDays(-2), Today).ShouldBe("позавчера");
        QuickEntryFormatter.Date(new DateOnly(2026, 10, 1), Today).ShouldBe("1 октября");
        QuickEntryFormatter.Date(new DateOnly(2025, 12, 31), Today).ShouldBe("31 декабря 2025");
    }

    [Fact]
    public async Task ReloadContext_KeepsTheLineAndItsPreview()
    {
        var viewModel = await OpenAsync();
        viewModel.Input = "350 еда";

        await viewModel.ReloadContextAsync(Ct);

        viewModel.Input.ShouldBe("350 еда");
        viewModel.Headline.ShouldBe("Расход 350 ₽");
        viewModel.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ReceiptInAnotherCurrency_IsPreviewedInIt_WithTheAccountCurrency()
    {
        var viewModel = await OpenAsync();

        viewModel.Input = "20 eur еда ужин";

        viewModel.Headline.ShouldBe("Расход 20 €");
        viewModel.Details.ShouldBe("Еда · Наличные, в RUB по курсу ЦБ · сегодня · «ужин»");
        viewModel.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Formatter_Money() =>
        QuickEntryFormatter.Money(1234567.5m, CurrencyCode.Usd).ShouldBe("1 234 567,50 $");

    [Fact]
    public void Formatter_Money_WholeAmountsHaveNoKopecks() =>
        QuickEntryFormatter.Money(350m, CurrencyCode.Rub).ShouldBe("350 ₽");

    private sealed class FakeBackend : IQuickEntryBackend
    {
        public List<string> SavedLines { get; } = [];

        public Exception? SaveError { get; set; }

        public Exception? LoadError { get; set; }

        public Task<QuickEntryContext> LoadContextAsync(CancellationToken cancellationToken = default) =>
            LoadError is not null
                ? Task.FromException<QuickEntryContext>(LoadError)
                : Task.FromResult(new QuickEntryContext(Categories, [Cash], new Dictionary<TransactionType, Guid>(), CurrencyCode.Rub, CategoryHints.Empty, Today));

        public Task<SavedTransaction> SaveAsync(string line, CancellationToken cancellationToken = default)
        {
            if (SaveError is not null)
            {
                return Task.FromException<SavedTransaction>(SaveError);
            }

            SavedLines.Add(line);
            var food = Categories.First(c => c.Name == "Еда");
            return Task.FromResult(new SavedTransaction(
                Transaction.Create(Today, TransactionType.Expense, 350m, 1m, food, Cash, null, DateTime.UtcNow), false));
        }
    }
}
