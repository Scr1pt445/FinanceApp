using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Core.Tests.Presentation;

public class DebtsAndCompareViewModelTests : FinanceViewModelTestBase
{
    private static string Rub(decimal amount) => Format.Money(amount, CurrencyCode.Rub);

    private async Task<DebtsViewModel> DebtsAsync()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var debts = new DebtsViewModel(Backend, Time);
        await debts.LoadAsync(Ct);
        return debts;
    }

    private static async Task SaveAsync(DebtsViewModel debts, DebtEditor editor, Action<DebtEditor> fill)
    {
        fill(editor);
        editor.SaveCommand.Execute(null);
        await WaitClosedAsync(debts);
    }

    /// <summary>The editor closes once the backend call and the reload are done.</summary>
    private static async Task WaitClosedAsync(DebtsViewModel debts)
    {
        for (var i = 0; i < 100 && debts.IsEditing; i++)
        {
            await Task.Delay(10, Ct);
        }

        debts.IsEditing.ShouldBeFalse();
    }

    [Fact]
    public async Task Debts_AddLent_ShowsTotalsAndSummary()
    {
        var debts = await DebtsAsync();
        debts.IsEmpty.ShouldBeTrue();
        debts.Summary.ShouldBe("Кто кому должен и до какого числа");

        debts.NewCommand.Execute(null);
        var editor = debts.Editor.ShouldNotBeNull();
        editor.IsNew.ShouldBeTrue();
        editor.Problem.ShouldNotBeNull();
        await SaveAsync(debts, editor, e =>
        {
            e.Person = "Саша";
            e.AmountText = "5000";
            e.DueText = "20.10.2026";
        });

        var item = debts.Open.ShouldHaveSingleItem();
        item.Person.ShouldBe("Саша");
        item.IsLent.ShouldBeTrue();
        item.DueText.ShouldBe("вернуть до 20 октября");
        debts.LentText.ShouldBe(Rub(5_000m));
        debts.BorrowedText.ShouldBeEmpty();
        debts.Summary.ShouldBe($"Мне должны {Rub(5_000m)}");
    }

    [Fact]
    public async Task Debts_Borrowed_Overdue_IsFlagged()
    {
        await Backend.AddDebtAsync(new(DebtDirection.Borrowed, "Банк", 3_000m, CurrencyCode.Rub, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12), null), Ct);
        var debts = await DebtsAsync();

        var item = debts.Open.ShouldHaveSingleItem();
        item.IsOverdue.ShouldBeTrue();
        item.DueText.ShouldStartWith("просрочен на 3 дня");
        item.Details.ShouldStartWith("взял 1 октября");
        debts.HasOverdue.ShouldBeTrue();
        debts.Summary.ShouldBe($"Я должен {Rub(3_000m)} · 1 просрочен");
    }

    [Fact]
    public async Task Debts_RepayInParts_ThenAll_MovesToClosed()
    {
        await Backend.AddDebtAsync(new(DebtDirection.Lent, "Саша", 5_000m, CurrencyCode.Rub, new DateOnly(2026, 10, 1), null, "на билеты"), Ct);
        var debts = await DebtsAsync();

        debts.EditCommand.Execute(debts.Open.Single());
        var editor = debts.Editor.ShouldNotBeNull();
        editor.CanRepay.ShouldBeTrue();
        editor.RepayText.ShouldBe("5000");
        editor.RepayText = "2000";
        editor.RepayCommand.Execute(null);
        await WaitClosedAsync(debts);

        var item = debts.Open.ShouldHaveSingleItem();
        item.AmountText.ShouldBe($"{Rub(3_000m)} из {Rub(5_000m)}");
        item.Details.ShouldBe("дал 1 октября · на билеты");

        debts.EditCommand.Execute(item);
        debts.Editor!.RepaidText.ShouldBe($"Уже вернули {Rub(2_000m)} из {Rub(5_000m)}");
        debts.Editor.RepayAllCommand.Execute(null);
        await WaitClosedAsync(debts);

        debts.Open.ShouldBeEmpty();
        debts.HasClosed.ShouldBeTrue();
        debts.Closed.ShouldHaveSingleItem().DueText.ShouldBe("вернули сегодня");
        debts.Summary.ShouldBe("Кто кому должен и до какого числа");
    }

    [Fact]
    public async Task Debts_ARefusedChange_StaysInTheEditor()
    {
        await Backend.AddDebtAsync(new(DebtDirection.Lent, "Саша", 5_000m, CurrencyCode.Rub, new DateOnly(2026, 10, 1), null, null), Ct);
        var debts = await DebtsAsync();
        debts.EditCommand.Execute(debts.Open.Single());
        var editor = debts.Editor!;

        editor.RepayText = "9000";
        editor.RepayCommand.Execute(null);
        for (var i = 0; i < 100 && editor.Error.Length == 0; i++)
        {
            await Task.Delay(10, Ct);
        }

        editor.Error.ShouldContain("больше остатка");
        debts.Editor.ShouldBeSameAs(editor);
    }

    [Fact]
    public async Task Compare_ThisMonthAgainstThePrevious_ToTheSameDay()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Backend.Earn("2026-10-01", 100_000m);
        Backend.Spend("2026-10-03", 12_000m, "Еда");
        Backend.Spend("2026-10-04", 5_000m, "Развлечения");
        Backend.Earn("2026-09-01", 90_000m);
        Backend.Spend("2026-09-03", 10_000m, "Еда");
        Backend.Spend("2026-09-28", 40_000m, "Жильё"); // after the 15th: left out

        var compare = new CompareMonthsViewModel(Backend, Time);
        await compare.LoadAsync(Ct);

        compare.MonthTitle.ShouldBe("Октябрь 2026");
        compare.OtherTitle.ShouldBe("Сентябрь 2026");
        compare.ShowsSamePoint.ShouldBeTrue();
        compare.ThroughText.ShouldContain("по 15-е число");
        compare.Headline.ShouldBe($"Расходы выросли на {Rub(7_000m)} (+70 %)");
        compare.HeadlineTone.ShouldBe(NoteTone.Bad);
        compare.Highlights.ShouldContain($"Больше всего выросли: Развлечения +{Rub(5_000m)}, Еда +{Rub(2_000m)}");

        var expenses = compare.Totals.Single(r => r.Label == "Расходы");
        expenses.AmountText.ShouldBe(Rub(17_000m));
        expenses.OtherText.ShouldBe(Rub(10_000m));
        expenses.Tone.ShouldBe(NoteTone.Bad);
        compare.Expenses.Select(e => e.Name).ShouldBe(["Еда", "Развлечения"]);
        compare.Expenses[1].ChangeText.ShouldBe("новое");

        compare.HasForecast.ShouldBeTrue();
        compare.ForecastCards[0].Title.ShouldBe("Можно тратить в день");

        compare.SamePoint = false;
        await compare.LoadAsync(Ct);
        compare.Totals.Single(r => r.Label == "Расходы").OtherText.ShouldBe(Rub(50_000m));
        compare.Headline.ShouldStartWith("Расходы снизились");
    }

    [Fact]
    public async Task Compare_StepsMonths_NeverPastToday_NorOntoEachOther()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var compare = new CompareMonthsViewModel(Backend, Time);
        await compare.LoadAsync(Ct);

        compare.NextMonthCommand.CanExecute(null).ShouldBeFalse();
        compare.NextOtherCommand.CanExecute(null).ShouldBeFalse(); // stepping over October would be November

        await compare.PreviousMonthCommand.ExecuteAsync(null);
        compare.Month.ShouldBe(new YearMonth(2026, 9));
        compare.Other.ShouldBe(new YearMonth(2026, 8));
        compare.HasForecast.ShouldBeFalse();
        compare.ShowsSamePoint.ShouldBeFalse();

        await compare.SwapCommand.ExecuteAsync(null);
        compare.Month.ShouldBe(new YearMonth(2026, 8));
        compare.Other.ShouldBe(new YearMonth(2026, 9));

        await compare.PreviousOtherCommand.ExecuteAsync(null);
        compare.Other.ShouldBe(new YearMonth(2026, 7)); // over August, not onto it
    }

    [Fact]
    public async Task Compare_Forecast_ListsBillsAndLimits()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Backend.Earn("2026-10-01", 100_000m);
        Backend.Spend("2026-10-05", 20_000m, "Еда");
        await Backend.SetCategoryLimitAsync(Backend.Named("Еда").Id, 30_000m, Ct);
        await Backend.AddRecurringAsync(new RecurringInput(
            TransactionType.Expense, 30_000m, Backend.Named("Жильё").Id, Backend.Card.Id, null, "Аренда", RecurrenceFrequency.Monthly, new DateOnly(2026, 10, 20)), Ct);

        var compare = new CompareMonthsViewModel(Backend, Time);
        await compare.LoadAsync(Ct);

        compare.ForecastCards[0].ValueText.ShouldBe(Rub(2_941m)); // (100 000 − 20 000 − 30 000) / 17
        var rent = compare.Planned.ShouldHaveSingleItem();
        rent.Title.ShouldBe("Аренда");
        rent.Subtitle.ShouldBe("20 октября");
        rent.AmountText.ShouldBe("−" + Rub(30_000m));
        var food = compare.LimitForecasts.ShouldHaveSingleItem();
        food.Title.ShouldBe("Еда");
        food.Note.ShouldStartWith("при таком темпе кончится 23 октября");
        food.Tone.ShouldBe(NoteTone.Bad);
    }
}
