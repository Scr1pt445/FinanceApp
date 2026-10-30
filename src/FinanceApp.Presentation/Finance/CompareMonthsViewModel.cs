using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Accounts;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>A line of the totals: «Расходы — 52 000 ₽ | 47 000 ₽ — +5 000 ₽».</summary>
public sealed record CompareRow(string Label, string AmountText, string OtherText, string ChangeText, NoteTone Tone);

/// <summary>A category in both months with two bars, 0..1 of the biggest amount on the screen.</summary>
public sealed record CompareCategoryRow(string Name, string AmountText, string OtherText, string ChangeText, NoteTone Tone, double Share, double OtherShare);

/// <summary>A category limit at the current pace: «Кафе», «при таком темпе кончится 22 ноября».</summary>
public sealed record LimitForecastRow(string Title, string Text, string Note, NoteTone Tone);

/// <summary>
/// «Сравнение»: any two months side by side — totals, categories and what changed most — and, for the current month,
/// where it is heading: what can be spent a day, the expected spending and free money, the bills still to come and
/// the limits at this pace. A screen of its own, so the summary stays light.
/// </summary>
public sealed partial class CompareMonthsViewModel(IFinanceBackend backend, TimeProvider timeProvider) : ObservableObject
{
    private CurrencyCode _currency = CurrencyCode.Rub;
    private decimal _rate = 1m;
    private int _version;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthTitle), nameof(MonthColumn), nameof(ShowsSamePoint))]
    [NotifyCanExecuteChangedFor(nameof(NextMonthCommand), nameof(NextOtherCommand))]
    public partial YearMonth Month { get; private set; } = YearMonth.Of(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OtherTitle), nameof(OtherColumn), nameof(ShowsSamePoint))]
    [NotifyCanExecuteChangedFor(nameof(NextOtherCommand))]
    public partial YearMonth Other { get; private set; } = YearMonth.Of(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime)).Previous;

    /// <summary>When one of the months is the current one: count both to today's day of the month.</summary>
    [ObservableProperty]
    public partial bool SamePoint { get; set; } = true;

    public string MonthTitle => Long(Month);

    public string OtherTitle => Long(Other);

    /// <summary>Column headers: «Ноябрь», «Октябрь» (with the year when it is not this one).</summary>
    public string MonthColumn => Column(Month);

    public string OtherColumn => Column(Other);

    public bool ShowsSamePoint => Month != Other && (Month == Current || Other == Current);

    /// <summary>«Оба месяца — с 1-го по 15-е число» when compared to the same day.</summary>
    [ObservableProperty]
    public partial string ThroughText { get; private set; } = string.Empty;

    /// <summary>«Расходы выросли на 5 000 ₽ (+11 %)».</summary>
    [ObservableProperty]
    public partial string Headline { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial NoteTone HeadlineTone { get; private set; }

    /// <summary>«Больше всего выросли: Кафе +3 100 ₽» and the like.</summary>
    public ObservableCollection<string> Highlights { get; } = [];

    public ObservableCollection<CompareRow> Totals { get; } = [];

    public ObservableCollection<CompareCategoryRow> Expenses { get; } = [];

    public ObservableCollection<CompareCategoryRow> Incomes { get; } = [];

    [ObservableProperty]
    public partial bool OtherIsEmpty { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    // The forecast, for the current month only.
    [ObservableProperty]
    public partial bool HasForecast { get; private set; }

    public ObservableCollection<InsightCard> ForecastCards { get; } = [];

    [ObservableProperty]
    public partial string ForecastNote { get; private set; } = string.Empty;

    public ObservableCollection<AmountRow> Planned { get; } = [];

    public ObservableCollection<LimitForecastRow> LimitForecasts { get; } = [];

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    private YearMonth Current => YearMonth.Of(Today);

    /// <summary>Opens on this month against the previous one, unless the user has already chosen.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var version = ++_version;
        try
        {
            var comparison = await backend.LoadComparisonAsync(Month, Other, SamePoint, cancellationToken);
            var balances = await backend.LoadBalancesAsync(cancellationToken: cancellationToken);
            var catalog = comparison.Forecast is { Planned.Count: > 0 } ? await backend.LoadCatalogAsync(cancellationToken) : null;
            if (version != _version)
            {
                return;
            }

            (_currency, _rate) = (balances.DisplayCurrency, balances.DisplayRate);
            Apply(comparison, catalog);
            Status = string.Empty;
        }
#pragma warning disable CA1031 // Shown on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось сравнить: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task PreviousMonthAsync() => ShowAsync(Month.Previous, Other == Month.Previous ? Other.Previous : Other);

    [RelayCommand(CanExecute = nameof(CanNextMonth))]
    private Task NextMonthAsync() => ShowAsync(Month.Next, Other == Month.Next ? Month : Other);

    /// <summary>Back to this month against the previous one.</summary>
    [RelayCommand]
    private Task ResetAsync() => ShowAsync(Current, Current.Previous);

    /// <summary>The second month steps over the first one: a month is not compared with itself.</summary>
    [RelayCommand]
    private Task PreviousOtherAsync() => ShowAsync(Month, Other.Previous == Month ? Month.Previous : Other.Previous);

    [RelayCommand(CanExecute = nameof(CanNextOther))]
    private Task NextOtherAsync() => ShowAsync(Month, Other.Next == Month ? Month.Next : Other.Next);

    /// <summary>The two months change places.</summary>
    [RelayCommand]
    private Task SwapAsync() => ShowAsync(Other, Month);

    partial void OnSamePointChanged(bool value) => _ = LoadAsync();

    private bool CanNextMonth() => Month < Current;

    private bool CanNextOther() => (Other.Next == Month ? Month.Next : Other.Next) <= Current;

    private async Task ShowAsync(YearMonth month, YearMonth other)
    {
        Month = month;
        Other = other;
        await LoadAsync();
    }

    private void Apply(MonthComparison comparison, Catalog? catalog)
    {
        var a = comparison.Totals;
        var b = comparison.OtherTotals;
        IsEmpty = a.Income == 0 && a.Expense == 0 && a.IntoEnvelopes == 0 && !comparison.OtherHasData;
        OtherIsEmpty = !comparison.OtherHasData;
        ThroughText = comparison.Through is { } day ? $"Оба месяца — с 1-го по {day}-е число, чтобы сравнивать честно." : string.Empty;

        Totals.Clear();
        Totals.Add(Row("Доход", a.Income, b.Income, moreIsGood: true));
        Totals.Add(Row("Расходы", a.Expense, b.Expense, moreIsGood: false));
        if (a.IntoEnvelopes != 0 || b.IntoEnvelopes != 0)
        {
            Totals.Add(Row("В конверты", a.IntoEnvelopes, b.IntoEnvelopes, moreIsGood: true));
        }

        Totals.Add(Row("Свободно", a.Balance, b.Balance, moreIsGood: true, signed: true));

        Fill(Expenses, comparison.Expenses, moreIsGood: false);
        Fill(Incomes, comparison.Incomes, moreIsGood: true);
        ApplyHeadline(comparison);
        ApplyForecast(comparison.Forecast, catalog);
    }

    private void ApplyHeadline(MonthComparison comparison)
    {
        Highlights.Clear();
        if (!comparison.OtherHasData)
        {
            Headline = string.Empty;
            return;
        }

        var (now, then) = (comparison.Totals.Expense, comparison.OtherTotals.Expense);
        var change = now - then;
        var percent = then == 0 ? string.Empty : $" ({Percent(now, then)})";
        (Headline, HeadlineTone) = change switch
        {
            > 0 => ($"Расходы выросли на {Money(change)}{percent}", NoteTone.Bad),
            < 0 => ($"Расходы снизились на {Money(-change)}{percent}", NoteTone.Good),
            _ => ("Расходы такие же", NoteTone.Muted),
        };

        var grew = comparison.Expenses.Where(c => c.Change > 0).OrderByDescending(c => c.Change).Take(2).ToList();
        if (grew.Count > 0)
        {
            Highlights.Add("Больше всего выросли: " + string.Join(", ", grew.Select(c => $"{c.Name} +{Money(c.Change)}")));
        }

        var fell = comparison.Expenses.Where(c => c.Change < 0).OrderBy(c => c.Change).Take(2).ToList();
        if (fell.Count > 0)
        {
            Highlights.Add("Меньше потрачено: " + string.Join(", ", fell.Select(c => $"{c.Name} −{Money(-c.Change)}")));
        }

        var fresh = comparison.Expenses.Where(c => c.OtherAmount == 0 && c.Amount > 0).Select(c => c.Name).Take(3).ToList();
        if (fresh.Count > 0)
        {
            Highlights.Add($"Новое по сравнению с «{OtherColumn}»: " + string.Join(", ", fresh));
        }
    }

    private void ApplyForecast(MonthForecast? forecast, Catalog? catalog)
    {
        ForecastCards.Clear();
        Planned.Clear();
        LimitForecasts.Clear();
        HasForecast = forecast is not null;
        ForecastNote = string.Empty;
        if (forecast is null)
        {
            return;
        }

        var days = $"{forecast.DaysLeft} {DaysWord(forecast.DaysLeft)} до конца месяца";
        if (forecast.PerDay is { } perDay)
        {
            ForecastCards.Add(perDay > 0
                ? new InsightCard("Можно тратить в день", Money(perDay), days, NoteTone.Good)
                : new InsightCard("Можно тратить в день", "0", "расходы уже больше дохода", NoteTone.Bad));
        }
        else
        {
            ForecastCards.Add(new InsightCard("Можно тратить в день", "—", "дохода в этом месяце пока нет", NoteTone.Muted));
        }

        ForecastCards.Add(new InsightCard("Расходы к концу месяца", $"≈ {Money(forecast.ExpectedExpense)}", $"сейчас {Money(forecast.Totals.Expense)}", NoteTone.Muted));
        ForecastCards.Add(new InsightCard(
            "Свободно к концу месяца",
            $"≈ {Format.Balance(InDisplay(forecast.ExpectedBalance), _currency)}",
            forecast.ExpectedBalance < 0 ? "уйдёт больше, чем придёт" : "доход − расходы − конверты",
            forecast.ExpectedBalance < 0 ? NoteTone.Bad : NoteTone.Muted));
        if (forecast.Planned.Count > 0)
        {
            var bills = forecast.PlannedExpense + forecast.PlannedSaving;
            ForecastCards.Add(new InsightCard(
                "Ещё по плану",
                bills > 0 ? $"−{Money(bills)}" : $"+{Money(forecast.PlannedIncome)}",
                forecast.PlannedIncome > 0 && bills > 0 ? $"и +{Money(forecast.PlannedIncome)} придёт" : "регулярные операции",
                NoteTone.Muted));
        }

        ForecastNote = forecast.PerDay is > 0
            ? "В день — это доход месяца минус потраченное, отложенное и регулярные платежи до конца месяца, поровну на оставшиеся дни."
            : string.Empty;

        if (catalog is not null)
        {
            var today = Today;
            foreach (var item in forecast.Planned)
            {
                var sign = item.Operation.Type == TransactionType.Income ? "+" : "−";
                Planned.Add(new AmountRow(
                    RecurringText.Title(item.Operation, catalog),
                    item.Date < today ? $"{RecurringText.When(item.Date, today)} · не записано" : RecurringText.When(item.Date, today),
                    sign + Money(item.BaseAmount)));
            }
        }

        foreach (var limit in forecast.Limits)
        {
            var line = limit.Line;
            var spent = $"{Money(line.Spent)} из {Money(line.Limit)}";
            if (line.IsOver)
            {
                LimitForecasts.Add(new LimitForecastRow(line.Category.Name, spent, $"уже превышен на {Money(line.Spent - line.Limit)}", NoteTone.Bad));
            }
            else if (limit.RunsOutOn is { } day)
            {
                LimitForecasts.Add(new LimitForecastRow(
                    line.Category.Name,
                    spent,
                    $"при таком темпе кончится {day.ToString("d MMMM", Format.Russian)}; чтобы уложиться — не больше {Money(limit.PerDayLeft)} в день",
                    NoteTone.Bad));
            }
            else
            {
                LimitForecasts.Add(new LimitForecastRow(
                    line.Category.Name,
                    spent,
                    $"уложитесь: к концу месяца ≈ {Money(limit.Projected)}",
                    NoteTone.Good));
            }
        }
    }

    private CompareRow Row(string label, decimal a, decimal b, bool moreIsGood, bool signed = false)
    {
        var text = signed ? Format.Balance(InDisplay(a), _currency) : Money(a);
        var other = OtherIsEmpty ? "—" : signed ? Format.Balance(InDisplay(b), _currency) : Money(b);
        var (change, tone) = OtherIsEmpty ? (string.Empty, NoteTone.Muted) : Change(a, b, moreIsGood);
        return new CompareRow(label, text, other, change, tone);
    }

    private void Fill(ObservableCollection<CompareCategoryRow> rows, IReadOnlyList<CategoryComparison> lines, bool moreIsGood)
    {
        rows.Clear();
        var biggest = lines.Select(c => Math.Max(c.Amount, c.OtherAmount)).DefaultIfEmpty(0).Max();
        foreach (var line in lines)
        {
            var (change, tone) = OtherIsEmpty ? (string.Empty, NoteTone.Muted)
                : line.OtherAmount == 0 ? ("новое", moreIsGood ? NoteTone.Good : NoteTone.Bad)
                : line.Amount == 0 ? ("не было", moreIsGood ? NoteTone.Bad : NoteTone.Good)
                : Change(line.Amount, line.OtherAmount, moreIsGood);
            rows.Add(new CompareCategoryRow(
                line.Name,
                Money(line.Amount),
                OtherIsEmpty ? "—" : Money(line.OtherAmount),
                change,
                tone,
                biggest == 0 ? 0 : (double)(line.Amount / biggest),
                biggest == 0 ? 0 : (double)(line.OtherAmount / biggest)));
        }
    }

    /// <summary>«+5 000 ₽ · +11 %»; more spending is bad news, more income good.</summary>
    private (string Text, NoteTone Tone) Change(decimal a, decimal b, bool moreIsGood)
    {
        var difference = a - b;
        if (difference == 0)
        {
            return ("без изменений", NoteTone.Muted);
        }

        var text = (difference > 0 ? "+" : "−") + Money(Math.Abs(difference));
        if (b > 0 && a >= 0)
        {
            text += $" · {Percent(a, b)}";
        }

        return (text, difference > 0 == moreIsGood ? NoteTone.Good : NoteTone.Bad);
    }

    private static string Percent(decimal a, decimal b)
    {
        var rounded = decimal.Round((a - b) / b * 100, 0, MidpointRounding.AwayFromZero);
        return $"{(rounded > 0 ? "+" : rounded < 0 ? "−" : string.Empty)}{Math.Abs(rounded)} %";
    }

    private string Long(YearMonth month) => $"{Format.MonthName(month.Month)} {month.Year}";

    private string Column(YearMonth month) =>
        month.Year == Today.Year ? Format.MonthName(month.Month) : $"{Format.MonthShort(month)} {month.Year}";

    private decimal InDisplay(decimal baseAmount)
    {
        if (_rate == 1m)
        {
            return baseAmount;
        }

        var value = baseAmount * _rate;
        return decimal.Round(value, Math.Abs(value) >= 100 ? 0 : 2, MidpointRounding.AwayFromZero);
    }

    private string Money(decimal baseAmount) => Format.Money(InDisplay(baseAmount), _currency);

    private static string DaysWord(int days) => (days % 10, days % 100) switch
    {
        (1, not 11) => "день",
        (2 or 3 or 4, not (12 or 13 or 14)) => "дня",
        _ => "дней",
    };
}
