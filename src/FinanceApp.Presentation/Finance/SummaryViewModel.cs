using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>A period tab: «Неделя», «Месяц», …, «Всё время».</summary>
public sealed record PeriodChoice(string Title, PeriodKind Kind)
{
    public override string ToString() => Title;
}

/// <summary>A totals card with the change against the previous period.</summary>
public sealed record SummaryCard(string Title, string AmountText, string? ChangeText, bool ChangeIsGood)
{
    public bool HasChange => ChangeText is not null;

    /// <param name="moreIsGood">Higher income or balance is good; higher expenses are not.</param>
    public static SummaryCard Create(string title, decimal current, decimal? previous, bool moreIsGood, CurrencyCode currency, string periodWord)
    {
        if (previous is not { } before)
        {
            return new SummaryCard(title, Format.Balance(current, currency), null, true);
        }

        var change = current - before;
        var text = change == 0 ? $"как в прошлом {periodWord}" : $"{Format.Signed(change, currency)} к прошлому {periodWord}";
        return new SummaryCard(title, Format.Balance(current, currency), text, change == 0 || (change > 0) == moreIsGood);
    }
}

/// <summary>A horizontal bar of «expenses by category»; <see cref="Share"/> is 0..1 of the biggest category.</summary>
public sealed record CategoryBarItem(string Name, string AmountText, string ShareText, double Share);

/// <summary>A column pair of the month chart; heights are 0..1 of the biggest value on the chart.</summary>
public sealed record MonthBarItem(string Label, double IncomeHeight, double ExpenseHeight, string Tooltip);

/// <summary>
/// «Итоги» for any period (requirements, section 3.4): week, month, quarter, year, a custom range or all time,
/// compared with the previous period of the same length.
/// </summary>
public sealed partial class SummaryViewModel(IFinanceBackend backend, TimeProvider timeProvider) : ObservableObject
{
    public static IReadOnlyList<PeriodChoice> Choices { get; } =
    [
        new("Неделя", PeriodKind.Week),
        new("Месяц", PeriodKind.Month),
        new("Квартал", PeriodKind.Quarter),
        new("Год", PeriodKind.Year),
        new("Свой период", PeriodKind.Custom),
        new("Всё время", PeriodKind.AllTime),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(IsCustom))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
    public partial Period Period { get; private set; } = Period.MonthOf(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));

    [ObservableProperty]
    public partial PeriodChoice Choice { get; set; } = Choices[1];

    public string Title => Format.Period(Period);

    public bool IsCustom => Period.Kind == PeriodKind.Custom;

    [ObservableProperty]
    public partial string CustomFrom { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomTo { get; set; } = string.Empty;

    public ObservableCollection<SummaryCard> Cards { get; } = [];

    public ObservableCollection<MonthBarItem> Months { get; } = [];

    public ObservableCollection<CategoryBarItem> Categories { get; } = [];

    [ObservableProperty]
    public partial bool HasMonths { get; private set; }

    [ObservableProperty]
    public partial bool HasCategories { get; private set; }

    [ObservableProperty]
    public partial string AverageDailyText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var overview = await backend.LoadPeriodAsync(Period, cancellationToken);
            Apply(overview.Summary, overview.BaseCurrency);
            Status = string.Empty;
        }
#pragma warning disable CA1031 // Shown in place.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось посчитать итоги: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private Task PreviousAsync() => GoToAsync(Period.Previous!);

    [RelayCommand(CanExecute = nameof(HasNext))]
    private Task NextAsync() => GoToAsync(Period.Next!);

    [RelayCommand]
    private async Task ApplyCustomAsync()
    {
        if (!Format.TryParseDate(CustomFrom, out var from) || !Format.TryParseDate(CustomTo, out var to) || to < from)
        {
            Status = "Укажите даты «с» и «по» в формате дд.мм.гггг.";
            return;
        }

        await GoToAsync(Period.Custom(from, to));
    }

    partial void OnChoiceChanged(PeriodChoice value)
    {
        if (value.Kind == PeriodKind.Custom)
        {
            // Start from what is shown now; the user edits the dates.
            CustomFrom = Format.DateText(Period.Kind == PeriodKind.AllTime ? Today.AddDays(-29) : Period.Start);
            CustomTo = Format.DateText(Period.Kind == PeriodKind.AllTime || Period.End > Today ? Today : Period.End);
            _ = ApplyCustomAsync();
            return;
        }

        _ = GoToAsync(Period.Of(value.Kind, Period.Kind is PeriodKind.AllTime or PeriodKind.Custom ? Today : Anchor()));
    }

    private bool HasPrevious() => Period.Previous is not null;

    private bool HasNext() => Period.Next is { } next && next.Start <= Today;

    /// <summary>Switching week → month keeps the month the user was looking at, not today's.</summary>
    private DateOnly Anchor() => Period.Contains(Today) ? Today : Period.Start;

    private async Task GoToAsync(Period period)
    {
        Period = period;
        await LoadAsync();
    }

    private void Apply(PeriodSummary summary, CurrencyCode currency)
    {
        var word = Period.Kind switch
        {
            PeriodKind.Week => "неделе",
            PeriodKind.Month => "месяцу",
            PeriodKind.Quarter => "кварталу",
            PeriodKind.Year => "году",
            _ => "периоду",
        };
        var previous = summary.Previous;

        Cards.Clear();
        Cards.Add(SummaryCard.Create("Доходы", summary.Current.Income, previous?.Income, true, currency, word));
        Cards.Add(SummaryCard.Create("Расходы", summary.Current.Expense, previous?.Expense, false, currency, word));
        Cards.Add(SummaryCard.Create("В конверты", summary.Current.IntoEnvelopes, previous?.IntoEnvelopes, true, currency, word));
        Cards.Add(SummaryCard.Create("Остаток", summary.Current.Balance, previous?.Balance, true, currency, word));
        AverageDailyText = $"В среднем {Format.Money(summary.AverageDailyExpense, currency)} в день";

        Months.Clear();
        var max = summary.ByMonth.Count == 0 ? 0 : summary.ByMonth.Max(m => Math.Max(m.Totals.Income, m.Totals.Expense));
        foreach (var (month, totals) in summary.ByMonth)
        {
            Months.Add(new MonthBarItem(
                Format.MonthShort(month),
                max == 0 ? 0 : (double)(totals.Income / max),
                max == 0 ? 0 : (double)(totals.Expense / max),
                $"{Format.MonthName(month.Month)} {month.Year}: доходы {Format.Money(totals.Income, currency)}, расходы {Format.Money(totals.Expense, currency)}"));
        }

        HasMonths = Months.Count > 1;

        Categories.Clear();
        var spent = summary.ExpensesByCategory.Where(c => c.Amount > 0).ToList();
        var biggest = spent.Count == 0 ? 0 : spent.Max(c => c.Amount);
        foreach (var category in spent)
        {
            Categories.Add(new CategoryBarItem(
                category.CategoryName,
                Format.Money(category.Amount, currency),
                summary.ShareOf(category).ToString("P0", Format.Russian),
                biggest == 0 ? 0 : (double)(category.Amount / biggest)));
        }

        HasCategories = Categories.Count > 0;
    }
}
