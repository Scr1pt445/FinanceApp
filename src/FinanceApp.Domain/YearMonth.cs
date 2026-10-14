using System.Globalization;

namespace FinanceApp.Domain;

/// <summary>A calendar month — the unit of all summaries.</summary>
public readonly record struct YearMonth : IComparable<YearMonth>
{
    public YearMonth(int year, int month)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12)
        {
            throw new DomainException($"Некорректный месяц: {year}-{month}.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);

    public YearMonth Previous => Month == 1 ? new YearMonth(Year - 1, 12) : new YearMonth(Year, Month - 1);

    public YearMonth Next => Month == 12 ? new YearMonth(Year + 1, 1) : new YearMonth(Year, Month + 1);

    public static YearMonth Of(DateOnly date) => new(date.Year, date.Month);

    /// <summary>Parses <c>YYYY-MM</c>.</summary>
    public static YearMonth Parse(string value) =>
        DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? Of(date)
            : throw new DomainException($"Некорректный месяц: «{value}». Ожидается формат ГГГГ-ММ.");

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    public int CompareTo(YearMonth other) => (Year, Month).CompareTo((other.Year, other.Month));

    public static bool operator <(YearMonth left, YearMonth right) => left.CompareTo(right) < 0;

    public static bool operator >(YearMonth left, YearMonth right) => left.CompareTo(right) > 0;

    public static bool operator <=(YearMonth left, YearMonth right) => left.CompareTo(right) <= 0;

    public static bool operator >=(YearMonth left, YearMonth right) => left.CompareTo(right) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");
}
