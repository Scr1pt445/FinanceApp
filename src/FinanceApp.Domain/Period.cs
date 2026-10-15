namespace FinanceApp.Domain;

public enum PeriodKind
{
    Week,
    Month,
    Quarter,
    Year,
    Custom,
    AllTime,
}

/// <summary>
/// A range of days for summaries (requirements, section 3.4). Calendar periods step to their calendar
/// neighbours; a custom range steps by its own length; «all time» has no neighbours.
/// </summary>
public sealed record Period
{
    private Period(PeriodKind kind, DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new DomainException("Конец периода раньше начала.");
        }

        Kind = kind;
        Start = start;
        End = end;
    }

    public PeriodKind Kind { get; }

    public DateOnly Start { get; }

    /// <summary>Inclusive.</summary>
    public DateOnly End { get; }

    public int Days => End.DayNumber - Start.DayNumber + 1;

    /// <summary>The previous period of the same kind and length; <c>null</c> for «all time».</summary>
    public Period? Previous => Shift(-1);

    public Period? Next => Shift(1);

    public static Period AllTime { get; } = new(PeriodKind.AllTime, DateOnly.MinValue, DateOnly.MaxValue);

    /// <summary>Monday to Sunday.</summary>
    public static Period WeekOf(DateOnly day)
    {
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        return new Period(PeriodKind.Week, monday, monday.AddDays(6));
    }

    public static Period MonthOf(DateOnly day) => Of(YearMonth.Of(day));

    public static Period Of(YearMonth month) => new(PeriodKind.Month, month.FirstDay, month.LastDay);

    public static Period QuarterOf(DateOnly day)
    {
        var first = new DateOnly(day.Year, ((day.Month - 1) / 3 * 3) + 1, 1);
        return new Period(PeriodKind.Quarter, first, first.AddMonths(3).AddDays(-1));
    }

    public static Period YearOf(DateOnly day) => new(PeriodKind.Year, new DateOnly(day.Year, 1, 1), new DateOnly(day.Year, 12, 31));

    public static Period Custom(DateOnly start, DateOnly end) => new(PeriodKind.Custom, start, end);

    public static Period Of(PeriodKind kind, DateOnly day) =>
        kind switch
        {
            PeriodKind.Week => WeekOf(day),
            PeriodKind.Month => MonthOf(day),
            PeriodKind.Quarter => QuarterOf(day),
            PeriodKind.Year => YearOf(day),
            PeriodKind.AllTime => AllTime,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Custom periods need explicit dates."),
        };

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    /// <summary>Calendar months touched by the period (for month-by-month charts).</summary>
    public IEnumerable<YearMonth> Months(DateOnly? clampStart = null, DateOnly? clampEnd = null)
    {
        var from = YearMonth.Of(clampStart is { } s && s > Start ? s : Start);
        var to = YearMonth.Of(clampEnd is { } e && e < End ? e : End);
        for (var month = from; month <= to; month = month.Next)
        {
            yield return month;
        }
    }

    private Period? Shift(int direction)
    {
        try
        {
            return Kind switch
            {
                PeriodKind.Week => WeekOf(Start.AddDays(7 * direction)),
                PeriodKind.Month => MonthOf(Start.AddMonths(direction)),
                PeriodKind.Quarter => QuarterOf(Start.AddMonths(3 * direction)),
                PeriodKind.Year => YearOf(Start.AddYears(direction)),
                PeriodKind.Custom => Custom(Start.AddDays(Days * direction), End.AddDays(Days * direction)),
                _ => null,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
