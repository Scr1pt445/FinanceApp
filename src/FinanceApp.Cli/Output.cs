using System.Globalization;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Cli;

internal static class Output
{
    public static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static string Money(decimal amount) => amount.ToString("#,0.##", Russian);

    public static string Money(decimal amount, CurrencyCode currency) => $"{Money(amount)} {currency}";

    public static string TypeName(TransactionType type) =>
        type switch
        {
            TransactionType.Income => "доход",
            TransactionType.Expense => "расход",
            TransactionType.Saving => "отложено",
            _ => type.ToString(),
        };

    public static void Totals(string title, MonthTotals totals, CurrencyCode currency)
    {
        Console.WriteLine(title);
        Console.WriteLine($"  Доходы:   {Money(totals.Income, currency),20}");
        Console.WriteLine($"  Расходы:  {Money(totals.Expense, currency),20}");
        Console.WriteLine($"  Отложено: {Money(totals.Saving, currency),20}");
        Console.WriteLine($"  Остаток:  {Money(totals.Balance, currency),20}");
    }

    public static void Error(string message) => Console.Error.WriteLine($"Ошибка: {message}");
}
