using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Accounts;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>A period tab: «Неделя», «Месяц», …, «Всё время».</summary>
public sealed record PeriodChoice(string Title, PeriodKind Kind)
{
    public override string ToString() => Title;
}

/// <summary>Whether a note is good news, bad news or just information.</summary>
public enum NoteTone
{
    Muted,
    Good,
    Bad,
}

/// <summary>What a segment of the «where income went» bar stands for; the view picks its color.</summary>
public enum FlowTone
{
    First,
    Second,
    Other,
    Envelopes,
    Free,
}

/// <summary>A segment of the «where income went» bar; <see cref="Weight"/> is its amount.</summary>
public sealed record FlowSegment(string Label, string AmountText, double Weight, FlowTone Tone);

/// <summary>A small card: «Отложено 35 %», «Расходы … +14 % к 15 сентября».</summary>
public sealed record InsightCard(string Title, string ValueText, string Note, NoteTone Tone);

/// <summary>
/// The spending chart: expense columns and running totals of this and the previous period, all 0..1.
/// <see cref="Slots"/> is the number of columns of the whole period; lines may stop earlier (today).
/// </summary>
public sealed record PaceChartData(IReadOnlyList<double> Bars, IReadOnlyList<double> Current, IReadOnlyList<double> Previous, int Slots)
{
    public static PaceChartData Empty { get; } = new([], [], [], 0);
}

/// <summary>A category of expenses with its change against the previous period.</summary>
public sealed record CategoryRow(string Name, string AmountText, string ChangeText, NoteTone ChangeTone, double Share);

/// <summary>A row of «Крупные траты» or «Доходы».</summary>
public sealed record AmountRow(string Title, string Subtitle, string AmountText);

/// <summary>An envelope during the period: how much went in and whether its goal is on track.</summary>
public sealed record EnvelopeRow(string Name, double Progress, bool HasGoal, string Note, NoteTone Tone);

/// <summary>
/// «Итоги» for any period (requirements, section 3.4): where income went, the pace of spending against the
/// previous period, a forecast, categories with their changes, the biggest expenses and the envelopes.
/// </summary>
public sealed partial class SummaryViewModel(IFinanceBackend backend, TimeProvider timeProvider) : ObservableObject
{
    private const decimal DaysPerMonth = 30.44m;

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

    /// <summary>«15 из 31 дня» for an ongoing period.</summary>
    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    // «Куда ушёл доход»
    [ObservableProperty]
    public partial bool HasFlow { get; private set; }

    [ObservableProperty]
    public partial string IncomeText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OutflowText { get; private set; } = string.Empty;

    /// <summary>Lengths of the income and outflow bars, 0..1 of the longer one.</summary>
    [ObservableProperty]
    public partial double IncomeShare { get; private set; }

    [ObservableProperty]
    public partial double OutflowShare { get; private set; }

    public ObservableCollection<FlowSegment> FlowSegments { get; } = [];

    [ObservableProperty]
    public partial string FlowNote { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool FlowNoteIsWarning { get; private set; }

    public ObservableCollection<InsightCard> Cards { get; } = [];

    // «Темп трат»
    [ObservableProperty]
    public partial string PaceTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PaceCaption { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial PaceChartData Pace { get; private set; } = PaceChartData.Empty;

    [ObservableProperty]
    public partial bool HasPreviousPace { get; private set; }

    [ObservableProperty]
    public partial string PaceStartLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PaceEndLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PreviousLegend { get; private set; } = string.Empty;

    public ObservableCollection<CategoryRow> Categories { get; } = [];

    public ObservableCollection<AmountRow> TopExpenses { get; } = [];

    public ObservableCollection<AmountRow> Incomes { get; } = [];

    public ObservableCollection<EnvelopeRow> Envelopes { get; } = [];

    public ObservableCollection<string> EnvelopeWarnings { get; } = [];

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var overview = await backend.LoadPeriodAsync(Period, cancellationToken);
            var balances = await backend.LoadBalancesAsync(cancellationToken: cancellationToken);
            Apply(overview, balances);
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

    /// <summary>«в прошлом месяце», «на прошлой неделе»…</summary>
    private string InPrevious => Period.Kind switch
    {
        PeriodKind.Week => "на прошлой неделе",
        PeriodKind.Month => "в прошлом месяце",
        PeriodKind.Quarter => "в прошлом квартале",
        PeriodKind.Year => "в прошлом году",
        _ => "в прошлом периоде",
    };

    /// <summary>«к прошлому месяцу», «к прошлой неделе»…</summary>
    private string ToPrevious => Period.Kind switch
    {
        PeriodKind.Week => "к прошлой неделе",
        PeriodKind.Month => "к прошлому месяцу",
        PeriodKind.Quarter => "к прошлому кварталу",
        PeriodKind.Year => "к прошлому году",
        _ => "к прошлому периоду",
    };

    private void Apply(PeriodOverview overview, BalancesOverview balances)
    {
        var currency = overview.BaseCurrency;
        var summary = overview.Summary;
        var insights = overview.Insights;
        var totals = summary.Current;

        IsEmpty = overview.Transactions.Count == 0;
        ProgressText = insights.IsOngoing ? $"{insights.ElapsedDays} из {Period.Days} {DaysWord(Period.Days)}" : string.Empty;

        ApplyFlow(totals, insights, currency);
        ApplyCards(summary, insights, currency);
        ApplyPace(insights, currency);
        ApplyCategories(summary, insights.PreviousCumulative.Count > 0, currency);
        ApplyLists(overview, currency);
        ApplyEnvelopes(insights, balances);
    }

    private void ApplyFlow(PeriodTotals totals, PeriodInsights insights, CurrencyCode currency)
    {
        FlowSegments.Clear();
        var spent = totals.Expense - totals.ExpenseFromEnvelopes;
        var intoEnvelopes = Math.Max(totals.IntoEnvelopes, 0);
        var free = Math.Max(totals.Balance, 0);
        var outflow = spent + intoEnvelopes;
        HasFlow = totals.Income > 0 || outflow > 0;

        var top = insights.SpentFromAccountsByCategory.Where(c => c.Amount > 0).Take(2).ToList();
        var tones = new[] { FlowTone.First, FlowTone.Second };
        for (var i = 0; i < top.Count; i++)
        {
            FlowSegments.Add(new FlowSegment(top[i].CategoryName, Format.Money(top[i].Amount, currency), (double)top[i].Amount, tones[i]));
        }

        var other = spent - top.Sum(c => c.Amount);
        if (other > 0)
        {
            FlowSegments.Add(new FlowSegment("остальное", Format.Money(other, currency), (double)other, FlowTone.Other));
        }

        if (intoEnvelopes > 0)
        {
            FlowSegments.Add(new FlowSegment("в конверты", Format.Money(intoEnvelopes, currency), (double)intoEnvelopes, FlowTone.Envelopes));
        }

        if (free > 0)
        {
            FlowSegments.Add(new FlowSegment("осталось", Format.Money(free, currency), (double)free, FlowTone.Free));
        }

        // The lower bar is outflow plus what is left, so it is never shorter than income.
        var lower = outflow + free;
        var longest = Math.Max(totals.Income, lower);
        IncomeText = $"Доход {Format.Money(totals.Income, currency)}";
        OutflowText = $"Потрачено {Format.Money(spent, currency)}" + (intoEnvelopes > 0 ? $" · в конверты {Format.Money(intoEnvelopes, currency)}" : string.Empty);
        IncomeShare = longest == 0 ? 0 : (double)(totals.Income / longest);
        OutflowShare = longest == 0 ? 0 : (double)(lower / longest);

        var notes = new List<string>();
        FlowNoteIsWarning = totals.Balance < 0;
        if (totals.Balance < 0)
        {
            notes.Add(totals.Income == 0
                ? $"Дохода не было — {Format.Money(-totals.Balance, currency)} взято из остатков на счетах"
                : $"Потрачено и отложено на {Format.Money(-totals.Balance, currency)} больше дохода — взято из остатков на счетах");
        }
        else if (totals.Income > 0)
        {
            notes.Add($"Свободными осталось {Format.Money(totals.Balance, currency)}");
        }

        if (totals.ExpenseFromEnvelopes > 0)
        {
            notes.Add($"ещё {Format.Money(totals.ExpenseFromEnvelopes, currency)} потрачено из конвертов");
        }

        if (totals.IntoEnvelopes < 0)
        {
            notes.Add($"из конвертов взято {Format.Money(-totals.IntoEnvelopes, currency)}");
        }

        FlowNote = string.Join("; ", notes);
    }

    private void ApplyCards(PeriodSummary summary, PeriodInsights insights, CurrencyCode currency)
    {
        var totals = summary.Current;

        // A previous period without any transactions is before tracking started, not a period of zeros.
        var previous = insights.PreviousCumulative.Count > 0 ? summary.Previous : null;
        Cards.Clear();

        var saved = totals.Income > 0 && totals.IntoEnvelopes > 0 ? totals.IntoEnvelopes / totals.Income : 0;
        Cards.Add(new InsightCard(
            "Отложено",
            totals.Income > 0 ? saved.ToString("P0", Format.Russian) : "—",
            totals.IntoEnvelopes > 0 ? $"дохода · {Format.Money(totals.IntoEnvelopes, currency)} в конверты" : "в конверты ничего",
            NoteTone.Muted));

        // An ongoing period is compared with the same day of the previous one, a finished one with all of it.
        var (note, tone) = (string.Empty, NoteTone.Muted);
        if (insights.IsOngoing && insights.PreviousToSamePoint is { } samePoint && Period.Previous is { } before)
        {
            var day = before.Start.AddDays(insights.ElapsedDays - 1);
            (note, tone) = Change(totals.Expense, samePoint, $"к {day.ToString("d MMMM", Format.Russian)}");
        }
        else if (previous is not null)
        {
            (note, tone) = Change(totals.Expense, previous.Expense, ToPrevious);
        }

        Cards.Add(new InsightCard("Расходы", Format.Money(totals.Expense, currency), note, tone));

        var dailyNote = string.Empty;
        if (previous is not null && Period.Previous is { } prior)
        {
            dailyNote = $"{InPrevious} {Format.Money(decimal.Round(previous.Expense / prior.Days, 0, MidpointRounding.AwayFromZero), currency)}";
        }

        Cards.Add(new InsightCard("В среднем в день", Format.Money(decimal.Round(summary.AverageDailyExpense, 0, MidpointRounding.AwayFromZero), currency), dailyNote, NoteTone.Muted));

        if (insights.Forecast is { } forecast)
        {
            Cards.Add(new InsightCard(
                Period.Kind switch
                {
                    PeriodKind.Week => "К концу недели",
                    PeriodKind.Month => "К концу месяца",
                    PeriodKind.Quarter => "К концу квартала",
                    PeriodKind.Year => "К концу года",
                    _ => "К концу периода",
                },
                $"≈ {Format.Money(RoundForecast(forecast), currency)}",
                insights.PreviousToSamePoint is > 0 ? $"если дальше как {InPrevious}" : "при таком темпе",
                NoteTone.Muted));
        }
        else
        {
            Cards.Add(new InsightCard(
                "Свободный остаток",
                Format.Balance(totals.Balance, currency),
                "доход − расходы − конверты",
                totals.Balance < 0 ? NoteTone.Bad : NoteTone.Muted));
        }
    }

    private void ApplyPace(PeriodInsights insights, CurrencyCode currency)
    {
        var buckets = insights.Buckets;
        PaceTitle = insights.BucketSize switch
        {
            BucketSize.Day => "Темп трат по дням",
            BucketSize.Week => "Темп трат по неделям",
            _ => "Темп трат по месяцам",
        };

        var maxBar = buckets.Count == 0 ? 0 : buckets.Max(b => b.Expense);
        var maxLine = insights.Cumulative.Concat(insights.PreviousCumulative).DefaultIfEmpty(0).Max();
        Pace = new PaceChartData(
            buckets.Select(b => maxBar == 0 ? 0 : (double)(b.Expense / maxBar)).ToList(),
            insights.Cumulative.Select(v => maxLine == 0 ? 0 : (double)(v / maxLine)).ToList(),
            insights.PreviousCumulative.Take(buckets.Count).Select(v => maxLine == 0 ? 0 : (double)(v / maxLine)).ToList(),
            buckets.Count);
        HasPreviousPace = insights.PreviousCumulative.Count > 0;
        PreviousLegend = Period.Kind switch
        {
            PeriodKind.Week => "прошлая неделя",
            PeriodKind.Month when Period.Previous is { } previous => Format.MonthName(previous.Start.Month).ToLower(Format.Russian),
            PeriodKind.Year when Period.Previous is { } previous => $"{previous.Start.Year} год",
            _ => "прошлый период",
        };

        PaceStartLabel = buckets.Count == 0 ? string.Empty : Short(buckets[0].Start);
        PaceEndLabel = buckets.Count == 0 ? string.Empty : Short(buckets[^1].End);

        var spent = insights.Cumulative.DefaultIfEmpty(0).Last();
        var caption = insights.IsOngoing && Period.Kind != PeriodKind.AllTime
            ? $"{Format.Money(spent, currency)} к {Today.ToString("d MMMM", Format.Russian)}"
            : $"{Format.Money(spent, currency)} за период";
        if (insights.IsOngoing && insights.PreviousToSamePoint is { } samePoint && HasPreviousPace)
        {
            caption += $" · {InPrevious} к этому дню {Format.Money(samePoint, currency)}";
        }

        PaceCaption = caption;
    }

    private void ApplyCategories(PeriodSummary summary, bool hasHistory, CurrencyCode currency)
    {
        Categories.Clear();
        var spent = summary.ExpensesByCategory.Where(c => c.Amount > 0).ToList();
        var biggest = spent.Count == 0 ? 0 : spent.Max(c => c.Amount);
        foreach (var category in spent)
        {
            var (text, tone) = !hasHistory ? (string.Empty, NoteTone.Muted)
                : category.PreviousAmount == 0 ? ("новое", NoteTone.Bad)
                : Percent(category.Amount, category.PreviousAmount);
            Categories.Add(new CategoryRow(
                category.CategoryName,
                Format.Money(category.Amount, currency),
                text,
                tone,
                biggest == 0 ? 0 : (double)(category.Amount / biggest)));
        }
    }

    private void ApplyLists(PeriodOverview overview, CurrencyCode currency)
    {
        var names = overview.Categories.ToDictionary(c => c.Id, c => c.Name);
        var accounts = overview.Accounts.ToDictionary(a => a.Id);
        TopExpenses.Clear();
        foreach (var t in overview.Insights.TopExpenses)
        {
            var category = t.CategoryId is { } id && names.TryGetValue(id, out var name) ? name : "?";
            var title = string.IsNullOrWhiteSpace(t.Note) ? category : Capitalize(t.Note);
            var subtitle = Short(t.Date) + (title == category ? string.Empty : $" · {category}")
                + (accounts.TryGetValue(t.AccountId, out var account) && account.IsEnvelope ? $" · из «{account.Name}»" : string.Empty);
            TopExpenses.Add(new AmountRow(title, subtitle, Format.Money(t.BaseAmount, currency)));
        }

        Incomes.Clear();
        var income = overview.Summary.Current.Income;
        foreach (var source in overview.Insights.IncomeByCategory)
        {
            Incomes.Add(new AmountRow(source.CategoryName, income == 0 ? string.Empty : (source.Amount / income).ToString("P0", Format.Russian), Format.Money(source.Amount, currency)));
        }
    }

    private void ApplyEnvelopes(PeriodInsights insights, BalancesOverview balances)
    {
        Envelopes.Clear();
        EnvelopeWarnings.Clear();
        var months = Math.Max((decimal)Math.Min(insights.ElapsedDays, Period.Kind == PeriodKind.AllTime ? int.MaxValue : Period.Days) / DaysPerMonth, 1m);

        foreach (var balance in balances.Envelopes)
        {
            var account = balance.Account;
            var flow = insights.EnvelopeFlows.GetValueOrDefault(account.Id);
            var parts = new List<string> { flow == 0 ? "без движения" : Format.Signed(flow, account.Currency) };
            var tone = NoteTone.Muted;

            if (account.GoalAmount is { } goal)
            {
                parts.Add($"{Format.Money(balance.Balance, account.Currency)} из {Format.Money(goal, account.Currency)}");
                if (balance.MonthlyToGoal(Today) is { } needed && account.GoalDate is { } date)
                {
                    var perMonth = decimal.Round(flow / months, 0, MidpointRounding.AwayFromZero);
                    if (perMonth >= needed)
                    {
                        parts.Add("в срок");
                        tone = NoteTone.Good;
                    }
                    else
                    {
                        tone = NoteTone.Bad;
                        var deadline = $"{Format.MonthName(date.Month).ToLower(Format.Russian)} {date.Year}";
                        EnvelopeWarnings.Add(
                            $"{account.Name}: "
                            + (perMonth > 0
                                ? $"при {Format.Money(perMonth, account.Currency)} в месяц цель наберётся через {MonthsToGoal(goal - balance.Balance, perMonth)} мес."
                                : "в этом периоде не пополнялся")
                            + $" — чтобы успеть к сроку ({deadline}), нужно {Format.Money(needed, account.Currency)} в месяц");
                    }
                }
                else if (balance.Balance >= goal)
                {
                    parts.Add("цель достигнута");
                    tone = NoteTone.Good;
                }
            }
            else
            {
                parts.Add(Format.Money(balance.Balance, account.Currency));
            }

            Envelopes.Add(new EnvelopeRow(account.Name, (double)(balance.GoalProgress ?? 0), account.GoalAmount is not null, string.Join(" · ", parts), tone));
        }
    }

    private static int MonthsToGoal(decimal left, decimal perMonth) => (int)Math.Ceiling(left / perMonth);

    /// <summary>«+14 %» is bad news for expenses, «−8 %» good.</summary>
    private static (string Text, NoteTone Tone) Percent(decimal current, decimal previous)
    {
        if (previous == 0)
        {
            return (string.Empty, NoteTone.Muted);
        }

        var change = (current - previous) / previous;
        var rounded = decimal.Round(change * 100, 0, MidpointRounding.AwayFromZero);
        return rounded == 0
            ? ("как раньше", NoteTone.Muted)
            : ($"{(rounded > 0 ? "+" : "−")}{Math.Abs(rounded)} %", rounded > 0 ? NoteTone.Bad : NoteTone.Good);
    }

    private static (string Text, NoteTone Tone) Change(decimal current, decimal previous, string against)
    {
        if (previous == 0)
        {
            return (string.Empty, NoteTone.Muted);
        }

        var (text, tone) = Percent(current, previous);
        return text == "как раньше" ? ($"0 % {against}", tone) : ($"{text} {against}", tone);
    }

    private static decimal RoundForecast(decimal value) =>
        value >= 10_000 ? decimal.Round(value / 1_000, 0, MidpointRounding.AwayFromZero) * 1_000 : decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string Short(DateOnly day) => $"{day.Day} {Format.MonthShort(YearMonth.Of(day))}";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Format.Russian) + text[1..];

    private static string DaysWord(int days) => (days % 10, days % 100) switch
    {
        (1, not 11) => "дня",
        _ => "дней",
    };
}
