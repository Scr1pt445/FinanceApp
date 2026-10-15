using System.Globalization;
using FinanceApp.Domain;
using FinanceApp.Presentation.QuickEntry;

namespace FinanceApp.Presentation.Finance;

/// <summary>Russian, banking-app style text for amounts, days and periods.</summary>
public static class Format
{
    public static CultureInfo Russian => QuickEntryFormatter.Russian;

    public static string Money(decimal amount, CurrencyCode currency) => QuickEntryFormatter.Money(amount, currency);

    /// <summary>«+50 000 ₽», «−350 ₽», «0 ₽».</summary>
    public static string Signed(decimal amount, CurrencyCode currency) =>
        amount switch
        {
            > 0 => "+" + Money(amount, currency),
            < 0 => "−" + Money(-amount, currency),
            _ => Money(0, currency),
        };

    /// <summary>«−1 200 ₽» for negatives, plain otherwise — for balances.</summary>
    public static string Balance(decimal amount, CurrencyCode currency) =>
        amount < 0 ? "−" + Money(-amount, currency) : Money(amount, currency);

    /// <summary>«Сегодня», «Вчера», «12 октября, пн», with the year when it is not this year.</summary>
    public static string Day(DateOnly day, DateOnly today) =>
        (today.DayNumber - day.DayNumber) switch
        {
            0 => "Сегодня",
            1 => "Вчера",
            _ => day.ToString(day.Year == today.Year ? "d MMMM, ddd" : "d MMMM yyyy, ddd", Russian),
        };

    public static string MonthName(int month) =>
        Capitalize(Russian.DateTimeFormat.MonthNames[month - 1]);

    /// <summary>Three letters for chart labels; the culture's own abbreviations are uneven («февр.», «март»).</summary>
    public static string MonthShort(YearMonth month) =>
        new[] { "янв", "фев", "мар", "апр", "май", "июн", "июл", "авг", "сен", "окт", "ноя", "дек" }[month.Month - 1];

    /// <summary>«Октябрь 2026», «2026 год», «IV квартал 2026», «12–18 октября 2026», «Всё время».</summary>
    public static string Period(Period period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return period.Kind switch
        {
            PeriodKind.Month => $"{MonthName(period.Start.Month)} {period.Start.Year}",
            PeriodKind.Year => $"{period.Start.Year} год",
            PeriodKind.Quarter => $"{new[] { "I", "II", "III", "IV" }[(period.Start.Month - 1) / 3]} квартал {period.Start.Year}",
            PeriodKind.AllTime => "Всё время",
            _ => Range(period.Start, period.End),
        };
    }

    /// <summary>«12–18 октября 2026», «28 сентября — 4 октября 2026», across years with both years.</summary>
    public static string Range(DateOnly start, DateOnly end)
    {
        if (start.Year != end.Year)
        {
            return $"{start.ToString("d MMMM yyyy", Russian)} — {end.ToString("d MMMM yyyy", Russian)}";
        }

        return start.Month == end.Month
            ? $"{start.Day}–{end.ToString("d MMMM yyyy", Russian)}"
            : $"{start.ToString("d MMMM", Russian)} — {end.ToString("d MMMM yyyy", Russian)}";
    }

    public static bool TryParseAmount(string? text, out decimal amount)
    {
        var normalized = (text ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
    }

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), ["dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "yyyy-MM-dd"], Russian, DateTimeStyles.None, out date);

    public static string DateText(DateOnly date) => date.ToString("dd.MM.yyyy", Russian);

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Russian) + text[1..];
}
