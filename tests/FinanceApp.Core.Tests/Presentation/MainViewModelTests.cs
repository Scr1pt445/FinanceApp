using FinanceApp.Application.Setup;
using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Presentation.Main;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Core.Tests.Presentation;

public class MainViewModelTests
{
    private static readonly List<Category> Categories = DefaultCatalog.CreateCategories().ToList();
    private static readonly Account Card = new(Guid.NewGuid(), "Карта", CurrencyCode.Rub, 0);
    private static readonly Account Cash = new(Guid.NewGuid(), "Наличные", CurrencyCode.Rub, 1);
    private static readonly Account Usd = new(Guid.NewGuid(), "Карта USD", CurrencyCode.Usd, 2);
    private static readonly Account PiggyBank = new(Guid.NewGuid(), "Копилка", CurrencyCode.Rub, 3, AccountKind.Envelope);

    private readonly FakeBackend _backend = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Category Named(string name, TransactionType type) => Categories.Single(c => c.Name == name && c.Type == type);

    private Transaction Add(string date, TransactionType type, decimal amount, string category, Account? account = null, string? note = null, decimal rate = 1m)
    {
        var t = Transaction.Create(
            DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), type, amount, rate,
            Named(category, type), account ?? Card, note, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(_backend.Stored.Count));
        _backend.Stored.Add(t);
        return t;
    }

    private async Task<MainViewModel> OpenAsync()
    {
        var viewModel = new MainViewModel(_backend, _time);
        await viewModel.LoadAsync(Ct);
        return viewModel;
    }

    [Fact]
    public async Task Opens_OnCurrentMonth_WithTotalsAgainstPreviousMonth()
    {
        Add("2026-10-01", TransactionType.Income, 50_000m, "Зарплата");
        Add("2026-10-07", TransactionType.Expense, 350m, "Еда", note: "кофе");
        _backend.Stored.Add(Transaction.CreateTransfer(new DateOnly(2026, 10, 8), 10_000m, 1m, Card, PiggyBank, null, null, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(_backend.Stored.Count)));
        Add("2026-09-20", TransactionType.Expense, 1_000m, "Еда");

        var viewModel = await OpenAsync();

        viewModel.Title.ShouldBe("Октябрь 2026");
        viewModel.Cards.Select(c => c.Title).ShouldBe(["Доходы", "Расходы", "В конверты", "Остаток"]);
        viewModel.Cards[1].AmountText.ShouldBe("350 ₽");
        viewModel.Cards[1].ChangeText.ShouldBe("−650 ₽ к прошлому месяцу");
        viewModel.Cards[1].ChangeIsGood.ShouldBeTrue();
        viewModel.Cards[3].AmountText.ShouldBe("39 650 ₽");
        viewModel.Rows.Count.ShouldBe(3);
        viewModel.Rows[0].Note.ShouldBeNull(); // newest first: the transfer into the envelope on 8 Oct
        viewModel.RowsSummary.ShouldBe("Операций: 3");
    }

    [Fact]
    public async Task CategoryBars_AreScaledToTheBiggest()
    {
        Add("2026-10-07", TransactionType.Expense, 300m, "Еда");
        Add("2026-10-08", TransactionType.Expense, 100m, "Транспорт");

        var viewModel = await OpenAsync();

        static string Percent(decimal share) => share.ToString("P0", FinanceApp.Presentation.QuickEntry.QuickEntryFormatter.Russian);
        viewModel.CategoryBars.Select(b => (b.Name, b.ShareText)).ShouldBe([("Еда", Percent(0.75m)), ("Транспорт", Percent(0.25m))]);
        viewModel.CategoryBars[0].Share.ShouldBe(1d);
        viewModel.CategoryBars[1].Share.ShouldBe(1d / 3, tolerance: 1e-9);
    }

    [Fact]
    public async Task Rows_ShowSignAndOriginalCurrency()
    {
        Add("2026-10-07", TransactionType.Expense, 20m, "Еда", Usd, "ужин", rate: 90m);
        Add("2026-10-06", TransactionType.Income, 1000m, "Зарплата");

        var viewModel = await OpenAsync();

        viewModel.Rows[0].AmountText.ShouldBe("−1 800 ₽");
        viewModel.Rows[0].OriginalAmountText.ShouldBe("20 $");
        viewModel.Rows[0].AccountName.ShouldBe("Карта USD");
        viewModel.Rows[1].AmountText.ShouldBe("+1 000 ₽");
        viewModel.Rows[1].OriginalAmountText.ShouldBeNull();
    }

    [Fact]
    public async Task Filters_CombineAndCanBeReset()
    {
        Add("2026-10-01", TransactionType.Expense, 100m, "Еда", Card, "кофе");
        Add("2026-10-02", TransactionType.Expense, 200m, "Еда", Cash, "обед");
        Add("2026-10-03", TransactionType.Expense, 300m, "Транспорт", Cash, "такси");
        Add("2026-10-04", TransactionType.Income, 400m, "Зарплата", Card);
        var viewModel = await OpenAsync();

        viewModel.TypeFilter = viewModel.TypeOptions.Single(o => o.Value == TransactionType.Expense);
        viewModel.Rows.Count.ShouldBe(3);

        viewModel.AccountFilter = viewModel.AccountOptions.Single(o => o.Title == "Наличные");
        viewModel.Rows.Count.ShouldBe(2);

        viewModel.SearchText = "ТАКС";
        viewModel.Rows.ShouldHaveSingleItem().Note.ShouldBe("такси");
        viewModel.RowsSummary.ShouldBe("Показано 1 из 4");

        viewModel.ResetFiltersCommand.Execute(null);
        viewModel.Rows.Count.ShouldBe(4);
    }

    [Fact]
    public async Task CategoryFilter_ByCategory()
    {
        Add("2026-10-01", TransactionType.Expense, 100m, "Еда");
        Add("2026-10-02", TransactionType.Expense, 300m, "Транспорт");
        var viewModel = await OpenAsync();

        viewModel.CategoryFilter = viewModel.CategoryOptions.Single(o => o.Title == "Транспорт · расход");

        viewModel.Rows.ShouldHaveSingleItem().CategoryName.ShouldBe("Транспорт");
    }

    [Fact]
    public async Task MonthNavigation_LoadsOtherMonths()
    {
        Add("2026-09-20", TransactionType.Expense, 1000m, "Еда");
        var viewModel = await OpenAsync();
        viewModel.IsEmpty.ShouldBeTrue();

        await viewModel.PreviousMonthCommand.ExecuteAsync(null);

        viewModel.Title.ShouldBe("Сентябрь 2026");
        viewModel.Rows.ShouldHaveSingleItem();

        await viewModel.CurrentMonthCommand.ExecuteAsync(null);
        viewModel.Title.ShouldBe("Октябрь 2026");
        _backend.LoadedMonths.ShouldBe([new YearMonth(2026, 10), new YearMonth(2026, 9), new YearMonth(2026, 10)]);
    }

    [Fact]
    public async Task Delete_ThenUndo_RestoresTheSameTransaction()
    {
        var coffee = Add("2026-10-07", TransactionType.Expense, 350m, "Еда", note: "кофе");
        var viewModel = await OpenAsync();

        await viewModel.DeleteCommand.ExecuteAsync(viewModel.Rows[0]);

        viewModel.Rows.ShouldBeEmpty();
        viewModel.CanUndo.ShouldBeTrue();
        viewModel.UndoText.ShouldBe("Удалено: −350 ₽ · Еда");

        await viewModel.UndoCommand.ExecuteAsync(null);

        viewModel.Rows.ShouldHaveSingleItem().Id.ShouldBe(coffee.Id);
        viewModel.CanUndo.ShouldBeFalse();
        viewModel.UndoText.ShouldBeEmpty();
    }

    [Fact]
    public async Task Undo_GoesBackwards_ThroughSeveralDeletions()
    {
        Add("2026-10-07", TransactionType.Expense, 100m, "Еда");
        Add("2026-10-08", TransactionType.Expense, 200m, "Еда");
        var viewModel = await OpenAsync();

        await viewModel.DeleteCommand.ExecuteAsync(viewModel.Rows[0]);
        await viewModel.DeleteCommand.ExecuteAsync(viewModel.Rows[0]);
        await viewModel.UndoCommand.ExecuteAsync(null);

        viewModel.Rows.ShouldHaveSingleItem().AmountText.ShouldBe("−100 ₽");
        viewModel.UndoText.ShouldBe("Удалено: −200 ₽ · Еда");
    }

    [Fact]
    public async Task Edit_SavesChangesThroughBackend()
    {
        var coffee = Add("2026-10-07", TransactionType.Expense, 350m, "Еда", note: "кофе");
        var viewModel = await OpenAsync();

        viewModel.EditCommand.Execute(viewModel.Rows[0]);
        var editor = viewModel.Editor.ShouldNotBeNull();
        editor.AmountText = "400,50";
        editor.Note = "капучино";
        editor.Account = editor.Accounts.Single(a => a.Name == "Наличные");
        await viewModel.SaveEditCommand.ExecuteAsync(null);

        viewModel.Editor.ShouldBeNull();
        var (id, input) = _backend.Updates.ShouldHaveSingleItem();
        id.ShouldBe(coffee.Id);
        input.Amount.ShouldBe(400.50m);
        input.Note.ShouldBe("капучино");
        input.AccountId.ShouldBe(Cash.Id);
    }

    [Fact]
    public async Task BackendError_IsShownInStatus()
    {
        Add("2026-10-07", TransactionType.Expense, 350m, "Еда");
        var viewModel = await OpenAsync();
        _backend.Failure = new DomainException("База занята");

        await viewModel.DeleteCommand.ExecuteAsync(viewModel.Rows[0]);

        viewModel.Status.ShouldBe("База занята");
        viewModel.CanUndo.ShouldBeFalse();
    }

    private sealed class FakeBackend : IMainBackend
    {
        public List<Transaction> Stored { get; } = [];

        public List<YearMonth> LoadedMonths { get; } = [];

        public List<(Guid Id, TransactionInput Input)> Updates { get; } = [];

        public Exception? Failure { get; set; }

        public Task<PeriodOverview> LoadMonthAsync(YearMonth month, CancellationToken cancellationToken = default)
        {
            LoadedMonths.Add(month);
            var period = Period.Of(month);
            var summary = SummaryCalculator.For(
                Stored, period, Categories.ToDictionary(c => c.Id), new[] { Card, Cash, Usd, PiggyBank }.ToDictionary(a => a.Id), new DateOnly(2026, 10, 15));
            var ofMonth = Stored.Where(t => month.Contains(t.Date)).OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ToList();
            return Task.FromResult(new PeriodOverview(period, ofMonth, summary, Categories, [Card, Cash, Usd, PiggyBank], CurrencyCode.Rub));
        }

        public Task<SavedTransaction> UpdateAsync(Guid id, TransactionInput input, CancellationToken cancellationToken = default)
        {
            Updates.Add((id, input));
            return Task.FromResult(new SavedTransaction(Stored.Single(t => t.Id == id), false));
        }

        public Task<Transaction> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException<Transaction>(Failure);
            }

            var t = Stored.Single(x => x.Id == id);
            Stored.Remove(t);
            return Task.FromResult(t);
        }

        public Task RestoreAsync(Transaction deleted, CancellationToken cancellationToken = default)
        {
            Stored.Add(deleted);
            return Task.CompletedTask;
        }
    }
}

public class TransactionEditorViewModelTests
{
    private static readonly List<Category> Categories = DefaultCatalog.CreateCategories().ToList();
    private static readonly Account Card = new(Guid.NewGuid(), "Карта", CurrencyCode.Rub);

    private static TransactionEditorViewModel Create()
    {
        var food = Categories.Single(c => c.Name == "Еда");
        var t = Transaction.Create(new DateOnly(2026, 10, 7), TransactionType.Expense, 350.5m, 1m, food, Card, "кофе", DateTime.UtcNow);
        return new TransactionEditorViewModel(t, Categories, [Card]);
    }

    [Fact]
    public void StartsWithTheTransactionValues()
    {
        var editor = Create();

        editor.Date.ShouldBe(new DateTime(2026, 10, 7));
        editor.AmountText.ShouldBe("350,5");
        editor.Category!.Name.ShouldBe("Еда");
        editor.Note.ShouldBe("кофе");
        editor.CanSave.ShouldBeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10,555")]
    [InlineData("abc")]
    public void InvalidAmount_BlocksSave(string amount)
    {
        var editor = Create();

        editor.AmountText = amount;

        editor.CanSave.ShouldBeFalse();
        editor.Problem.ShouldNotBeNull().ShouldContain("Сумма");
    }

    [Theory]
    [InlineData("1 200,50", 1200.50)]
    [InlineData("99.9", 99.9)]
    public void Amount_AcceptsSpacesCommaOrDot(string text, decimal expected)
    {
        var editor = Create();

        editor.AmountText = text;

        editor.ToInput().Amount.ShouldBe(expected);
    }

    [Fact]
    public void ChangingType_OffersCategoriesOfThatType()
    {
        var editor = Create();

        editor.Type = TransactionType.Income;

        editor.Categories.ShouldAllBe(c => c.Type == TransactionType.Income);
        editor.Category.ShouldNotBeNull().Type.ShouldBe(TransactionType.Income);
    }
}
