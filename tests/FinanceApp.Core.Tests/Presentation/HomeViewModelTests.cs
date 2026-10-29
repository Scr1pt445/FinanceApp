using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.QuickEntry;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Core.Tests.Presentation;

public class HomeViewModelTests
{
    private readonly FakeFinanceBackend _finance = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HomeViewModel> OpenAsync()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var quickEntry = new QuickEntryViewModel(new QuickEntryOnFinance(_finance)) { ReadyMessage = "Готово — сохранить" };
        var viewModel = new HomeViewModel(_finance, quickEntry, _time);
        await viewModel.OpenAsync(Ct);
        return viewModel;
    }

    [Fact]
    public async Task Open_ShowsTotalAndTheLastThirtyDaysByDay()
    {
        var envelope = _finance.Add(new Account(Guid.NewGuid(), "Машина", CurrencyCode.Rub, 5, AccountKind.Envelope));
        _finance.Earn("2026-10-01", 50_000);
        _finance.Move("2026-10-02", 3_000, _finance.Card, envelope);
        _finance.Spend("2026-10-14", 350, "Еда", note: "кофе");
        _finance.Spend("2026-10-14", 1_200, "Транспорт", _finance.Cash);
        _finance.Spend("2026-09-15", 100, "Еда"); // 31 days ago: only in the total

        var viewModel = await OpenAsync();

        viewModel.TotalText.ShouldBe("48 350 ₽");
        viewModel.TotalDetails.ShouldBe("Счета 45 350 ₽ · Конверты 3 000 ₽");
        viewModel.Warning.ShouldBeEmpty();
        viewModel.IsEmpty.ShouldBeFalse();
        viewModel.Recent.Select(d => (d.Title, d.TotalText)).ShouldBe([
            ("Вчера", "−1 550 ₽"),
            ("2 октября, пт", "0 ₽"),
            ("1 октября, чт", "+50 000 ₽"),
        ]);
        viewModel.Recent[0].Rows.Select(r => (r.Title, r.Subtitle, r.AmountText)).ShouldBe([
            ("Транспорт", "Наличные", "−1 200 ₽"),
            ("Еда", "кофе · Карта", "−350 ₽"),
        ]);
    }

    [Fact]
    public async Task NothingYet_IsEmpty()
    {
        var viewModel = await OpenAsync();

        viewModel.TotalText.ShouldBe("0 ₽");
        viewModel.Recent.ShouldBeEmpty();
        viewModel.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task Save_UpdatesTotalAndList_AndTheLineIsReadyForTheNextOne()
    {
        _finance.Earn("2026-10-01", 1_000);
        var viewModel = await OpenAsync();

        viewModel.QuickEntry.Input = "350 еда кофе";
        viewModel.QuickEntry.Message.ShouldBe("Готово — сохранить");
        await viewModel.QuickEntry.SaveCommand.ExecuteAsync(null);

        viewModel.LastSaved.ShouldBe("Сохранено: Расход 350 ₽");
        viewModel.TotalText.ShouldBe("650 ₽");
        viewModel.Recent[0].Title.ShouldBe("Сегодня");
        viewModel.Recent[0].Rows.ShouldHaveSingleItem().AmountText.ShouldBe("−350 ₽");
        viewModel.QuickEntry.Input.ShouldBeEmpty();
        viewModel.QuickEntry.Message.ShouldBe(QuickEntryViewModel.Hint);
    }

    [Fact]
    public async Task LoadFailure_IsShown_QuickEntryStillWorks()
    {
        _finance.LoadFailure = new IOException("база занята");

        var viewModel = await OpenAsync();

        viewModel.Warning.ShouldBe("Не удалось загрузить данные: база занята");
        viewModel.QuickEntry.Input = "350 еда";
        viewModel.QuickEntry.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    /// <summary>Quick entry that records into the same fake, so the home screen sees what was saved.</summary>
    private sealed class QuickEntryOnFinance(FakeFinanceBackend finance) : IQuickEntryBackend
    {
        public Task<QuickEntryContext> LoadContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuickEntryContext(
                finance.Categories, finance.Accounts, new Dictionary<TransactionType, Guid>(), CurrencyCode.Rub, CategoryHints.Empty, FakeFinanceBackend.Today));

        public Task<SavedTransaction> SaveAsync(string line, CancellationToken cancellationToken = default)
        {
            var draft = QuickEntryService.Preview(line, LoadContextAsync(cancellationToken).Result);
            var saved = finance.Spend("2026-10-15", draft.Amount!.Value, draft.Category!.Name, draft.Account, draft.Note);
            return Task.FromResult(new SavedTransaction(saved, false));
        }
    }
}
