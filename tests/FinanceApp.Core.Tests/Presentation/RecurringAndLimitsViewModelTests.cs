using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.QuickEntry;

namespace FinanceApp.Core.Tests.Presentation;

public class RecurringAndLimitsViewModelTests : FinanceViewModelTestBase
{
    private static readonly DateOnly Today = FakeFinanceBackend.Today;

    private async Task<HomeViewModel> HomeAsync()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var home = new HomeViewModel(Backend, new QuickEntryViewModel(new NoQuickEntry()), Time);
        await home.RefreshAsync(Ct);
        return home;
    }

    private RecurringOperation Rent(DateOnly next) =>
        Backend.AddRecurringAsync(new RecurringInput(TransactionType.Expense, 30_000m, Backend.Named("Жильё").Id, Backend.Card.Id, null, "Аренда", RecurrenceFrequency.Monthly, next)).Result;

    [Fact]
    public async Task Form_Repeat_SchedulesTheNextTime()
    {
        var form = TransactionFormViewModel.ForNew(Backend, await Backend.LoadCatalogAsync(Ct), Today, TransactionType.Expense, Backend.Card);
        form.AmountText = "30000";
        form.Note = "Аренда";
        form.ChooseCategoryCommand.Execute(form.Tiles.First(t => t.Name == "Жильё"));

        form.ChooseRepeatCommand.Execute(form.RepeatChoices.Single(r => r.Frequency == RecurrenceFrequency.Monthly));
        form.RepeatHint.ShouldStartWith("Следующая — 15 ноября 2026");
        await form.SaveCommand.ExecuteAsync(null);

        Backend.Transactions.ShouldHaveSingleItem();
        var rent = Backend.Recurring.ShouldHaveSingleItem();
        rent.NextDate.ShouldBe(new DateOnly(2026, 11, 15));
        rent.Note.ShouldBe("Аренда");
        rent.Amount.ShouldBe(30_000m);
    }

    [Fact]
    public async Task Home_OffersDueOperations_RecordsWithACorrectedAmount()
    {
        Rent(Today);
        Rent(Today.AddDays(3)); // not yet
        var home = await HomeAsync();

        var due = home.Due.ShouldHaveSingleItem();
        home.HasDue.ShouldBeTrue();
        due.Title.ShouldBe("Аренда");
        due.Details.ShouldBe("−30\u00a0000 ₽ · Карта · сегодня");
        due.AmountText = "31500";
        await due.RecordCommand.ExecuteAsync(null);

        Backend.Transactions.ShouldHaveSingleItem().Amount.ShouldBe(31_500m);
        home.Due.ShouldBeEmpty();
        home.LastSaved.ShouldStartWith("Записано:");
    }

    [Fact]
    public async Task Home_Skip_RecordsNothing()
    {
        Rent(Today.AddDays(-2));
        var home = await HomeAsync();

        await home.Due.Single().SkipCommand.ExecuteAsync(null);

        Backend.Transactions.ShouldBeEmpty();
        home.Due.ShouldBeEmpty();
        Backend.Recurring.Single().NextDate.ShouldBe(new DateOnly(2026, 11, 13));
    }

    [Fact]
    public async Task Home_WarnsOnlyAboutLimitsNearTheEnd()
    {
        Backend.Named("Еда").SetLimit(10_000m);
        Backend.Named("Транспорт").SetLimit(10_000m);
        Backend.Spend("2026-10-03", 8_500, "Еда");
        Backend.Spend("2026-10-03", 1_000, "Транспорт");

        var home = await HomeAsync();

        var warning = home.LimitWarnings.ShouldHaveSingleItem();
        warning.Title.ShouldBe("Еда");
        warning.SpentText.ShouldBe("8\u00a0500 ₽ из 10\u00a0000 ₽");
        warning.LeftText.ShouldBe("осталось 1\u00a0500 ₽");
        warning.IsNear.ShouldBeTrue();
    }

    [Fact]
    public async Task Summary_ShowsLimits_ForAMonth()
    {
        Backend.Named("Еда").SetLimit(5_000m);
        Backend.Spend("2026-10-03", 6_000, "Еда");
        var summary = new SummaryViewModel(Backend, Time);

        await summary.LoadAsync(Ct);

        var line = summary.Limits.ShouldHaveSingleItem();
        line.IsOver.ShouldBeTrue();
        line.LeftText.ShouldBe("перерасход 1\u00a0000 ₽");
        line.Progress.ShouldBe(1);
    }

    [Fact]
    public async Task Categories_SetAndRemoveALimit()
    {
        var categories = new CategoriesViewModel(Backend);
        await categories.LoadAsync(Ct);
        var food = categories.Expenses.Single(c => c.Name == "Еда");
        categories.Incomes.ShouldAllBe(c => !c.CanHaveLimit);

        food.StartLimitCommand.Execute(null);
        food.LimitInput = "30 000";
        await food.SaveLimitCommand.ExecuteAsync(null);

        Backend.Named("Еда").MonthlyLimit.ShouldBe(30_000m);
        categories.Expenses.Single(c => c.Name == "Еда").LimitText.ShouldBe("лимит 30\u00a0000 ₽ в месяц");

        await categories.Expenses.Single(c => c.Name == "Еда").RemoveLimitCommand.ExecuteAsync(null);
        Backend.Named("Еда").MonthlyLimit.ShouldBeNull();
    }

    [Fact]
    public async Task RecurringList_EditsAndDeletes()
    {
        Rent(Today.AddDays(5));
        var list = new RecurringViewModel(Backend, Time);
        var changed = 0;
        list.Changed += (_, _) => changed++;
        await list.LoadAsync(Ct);
        var item = list.Items.ShouldHaveSingleItem();
        item.ScheduleText.ShouldBe("каждый месяц, 20-го");
        item.NextText.ShouldBe("следующая — 20 октября");

        list.EditCommand.Execute(item);
        list.Editor!.AmountText = "32000";
        list.Editor.SaveCommand.Execute(null);
        await Task.Yield();

        Backend.Recurring.Single().Amount.ShouldBe(32_000m);
        list.EditCommand.Execute(list.Items.Single());
        list.Editor!.DeleteCommand.Execute(null);
        await Task.Yield();

        Backend.Recurring.ShouldBeEmpty();
        changed.ShouldBe(2);
    }

    private sealed class NoQuickEntry : IQuickEntryBackend
    {
        public Task<FinanceApp.Application.QuickEntry.QuickEntryContext> LoadContextAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FinanceApp.Application.Transactions.SavedTransaction> SaveAsync(string line, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
