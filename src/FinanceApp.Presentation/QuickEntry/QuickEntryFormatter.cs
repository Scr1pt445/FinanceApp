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

    public static string Money(decimal amount, CurrencyCode currency)
    {
        var symbol = currency.Value switch
        {
            "RUB" => "₽",
            "USD" => "$",
            "EUR" => "€",
            "BYN" => "Br",
            _ => currency.Value,
        };
        return $"{amount.ToString("#,0.##", Russian)} {symbol}";
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

        return saved.RateIsFallback ? text + " (курс из кэша — нет связи с ЦБ)" : text;
    }
}
