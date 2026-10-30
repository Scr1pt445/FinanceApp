using FinanceApp.Mobile.Controls;

namespace FinanceApp.Mobile.Tests;

/// <summary>The app's own calendar: the month grid, the text in the field and the sheet's answer.</summary>
public sealed class CalendarTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 10, 30);
    private MobileApp _app = null!;

    public async ValueTask InitializeAsync() => _app = await MobileApp.StartAsync();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public void Month_StartsOnMonday_InSixRows()
    {
        // 1 October 2026 is a Thursday.
        var cells = CalendarMonth.Layout(new DateOnly(2026, 10, 1));

        cells.Length.ShouldBe(42);
        cells[..3].ShouldAllBe(c => c == null);
        cells[3].ShouldBe(new DateOnly(2026, 10, 1));
        cells[33].ShouldBe(new DateOnly(2026, 10, 31));
        cells[34..].ShouldAllBe(c => c == null);
        CalendarMonth.Offset(new DateOnly(2026, 6, 1)).ShouldBe(0); // a Monday
    }

    [Theory]
    [InlineData("2026-10-30", "Сегодня, 30 октября")]
    [InlineData("2026-10-29", "Вчера, 29 октября")]
    [InlineData("2026-10-31", "Завтра, 31 октября")]
    [InlineData("2027-03-17", "17 марта 2027, ср")]
    public void Field_SaysTheDayLikeAPerson(string date, string expected) =>
        DateField.Describe(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), Today).ShouldBe(expected);

    [Fact]
    public void LongTitle_HasTheWeekDay() => CalendarMonth.Long(new DateOnly(2026, 3, 17)).ShouldBe("Вторник, 17 марта 2026");

    [Fact]
    public void GoalShortcuts_OfferDeadlines_AndNoDeadline()
    {
        var shortcuts = DateField.ShortcutsFor(DateFieldKind.Goal, Today, allowEmpty: true);

        shortcuts.Select(s => s.Title).ShouldBe(["Через 3 месяца", "Через полгода", "Через год", "К концу года", "Без срока"]);
        shortcuts[0].Date.ShouldBe(new DateOnly(2027, 1, 30));
        shortcuts[3].Date.ShouldBe(new DateOnly(2026, 12, 31));
        shortcuts[^1].Date.ShouldBeNull();
    }

    [Fact]
    public void Field_ShowsThePlaceholder_AndAClearButton_OnlyWhenAllowed()
    {
        var field = new DateField { Placeholder = "Без срока", AllowEmpty = true, Today = () => Today };
        field.Shown.ShouldBe("Без срока");

        field.Text = "17.03.2027";

        field.Shown.ShouldBe("17 марта 2027, ср");
    }

    [Fact]
    public async Task Sheet_TurnsMonths_AndAnswersWithThePickedDay()
    {
        var navigation = new ModalNavigation();
        var request = new CalendarRequest("Дата операции", Today, Today, DateField.ShortcutsFor(DateFieldKind.Operation, Today, false));

        var answer = CalendarSheet.ShowAsync(navigation, request);
        var sheet = navigation.ModalStack.ShouldHaveSingleItem().ShouldBeOfType<CalendarSheet>();
        sheet.MonthTitle.ShouldBe("Октябрь 2026");
        sheet.Step(1);
        sheet.MonthTitle.ShouldBe("Ноябрь 2026");
        sheet.Step(-2);
        sheet.MonthTitle.ShouldBe("Сентябрь 2026");
        sheet.Pick(new DateOnly(2026, 9, 12));

        (await answer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ShouldBe((true, new DateOnly(2026, 9, 12)));
        navigation.ModalStack.ShouldBeEmpty();
    }

    private sealed class ModalNavigation : INavigation
    {
        private readonly List<Page> _modal = [];

        public IReadOnlyList<Page> ModalStack => _modal;

        public IReadOnlyList<Page> NavigationStack => [];

        public Task PushModalAsync(Page page) => PushModalAsync(page, true);

        public Task PushModalAsync(Page page, bool animated)
        {
            _modal.Add(page);
            return Task.CompletedTask;
        }

        public Task<Page> PopModalAsync() => PopModalAsync(true);

        public Task<Page> PopModalAsync(bool animated)
        {
            var page = _modal[^1];
            _modal.RemoveAt(_modal.Count - 1);
            return Task.FromResult(page);
        }

        public void InsertPageBefore(Page page, Page before) => throw new NotSupportedException();

        public Task<Page> PopAsync() => throw new NotSupportedException();

        public Task<Page> PopAsync(bool animated) => throw new NotSupportedException();

        public Task PopToRootAsync() => throw new NotSupportedException();

        public Task PopToRootAsync(bool animated) => throw new NotSupportedException();

        public Task PushAsync(Page page) => throw new NotSupportedException();

        public Task PushAsync(Page page, bool animated) => throw new NotSupportedException();

        public void RemovePage(Page page) => throw new NotSupportedException();
    }
}
