using System.Globalization;
using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.QuickEntry;

/// <summary>Russian, human-friendly text for the live preview under the input line.</summary>
public static class QuickEntryFormatter
{
    public static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static string Type(TransactionType type) =>
        type switch
        {
            TransactionType.Income => "Доход",
            TransactionType.Expense => "Расход",
            TransactionType.Transfer => "Перевод",
            _ => type.ToString(),
        };

    /// <summary>The Belarusian ruble written as text where the sign cannot be drawn: the widget, files, the desktop.</summary>
    public const string BynText = "Br";

    /// <summary>
    /// The Belarusian ruble sign as the screen draws it. Unicode has no code point for it yet (approved by the National
    /// Bank in 2026), so it is «Br» by default; the phone puts here a private character that its own font draws as the
    /// sign («Б» with a stroke through the middle).
    /// </summary>
    public static string BynSign { get; set; } = BynText;

    /// <summary>Text for places that cannot use the app's font: the sign becomes «Br».</summary>
    public static string Plain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return BynSign == BynText ? text : text.Replace(BynSign, BynText, StringComparison.Ordinal);
    }

    /// <summary>«₽», «$», «Br»…; the code itself for currencies without a sign of their own.</summary>
    public static string Symbol(CurrencyCode currency) => currency.Value switch
    {
        "RUB" => "₽",
        "USD" => "$",
        "EUR" => "€",
        "BYN" => BynSign,
        "KZT" => "₸",
        "UAH" => "₴",
        "GBP" => "£",
        "CNY" => "¥",
        "TRY" => "₺",
        "GEL" => "₾",
        "AMD" => "֏",
        "AZN" => "₼",
        "PLN" => "zł",
        "INR" => "₹",
        "KRW" => "₩",
        "ILS" => "₪",
        _ => currency.Value,
    };

    public static string Money(decimal amount, CurrencyCode currency)
    {
        var symbol = Symbol(currency);
        // Whole amounts without kopecks, otherwise always two digits: «350 ₽», «350,50 ₽» — never «350,5 ₽».
        var format = decimal.Round(amount, 2) == decimal.Truncate(amount) ? "#,0" : "#,0.00";
        return $"{decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString(format, Russian)} {symbol}";
    }

    /// <summary>«сегодня», «вчера», «позавчера», otherwise «12 октября» (with the year if it is not this year).</summary>
    public static string Date(DateOnly date, DateOnly today) =>
        (today.DayNumber - date.DayNumber) switch
        {
            0 => "сегодня",
            1 => "вчера",
            2 => "позавчера",
            _ => date.Year == today.Year
                ? date.ToString("d MMMM", Russian)
                : date.ToString("d MMMM yyyy", Russian),
        };

    public static string Saved(SavedTransaction saved, CurrencyCode baseCurrency)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var t = saved.Transaction;
        var text = $"{Type(t.Type)} {Money(t.Amount, t.Currency)}";
        if (t.Currency != baseCurrency)
        {
            text += $" ≈ {Money(t.BaseAmount, baseCurrency)}";
        }

        return saved.RateIsFallback ? text + " (по последнему известному курсу ЦБ — нет связи)" : text;
    }
}
