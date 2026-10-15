using System.Globalization;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Presentation.QuickEntry;

namespace FinanceApp.Presentation.Main;

/// <summary>An entry of a filter dropdown; <see cref="Value"/> is <c>null</c> for «all».</summary>
public sealed record FilterOption<T>(string Title, T? Value)
{
    public override string ToString() => Title;
}

/// <summary>One line of the transaction list, already formatted for display.</summary>
public sealed record TransactionRow(
    Transaction Transaction,
    string DateText,
    string AmountText,
    string? OriginalAmountText,
    string CategoryName,
    string AccountName,
    string? Note)
{
    public TransactionType Type => Transaction.Type;

    public Guid Id => Transaction.Id;

    public static TransactionRow Create(Transaction t, IReadOnlyDictionary<Guid, string> categories, IReadOnlyDictionary<Guid, string> accounts, CurrencyCode baseCurrency)
    {
        ArgumentNullException.ThrowIfNull(t);
        var sign = t.Type switch
        {
            TransactionType.Income => "+",
            TransactionType.Expense => "−",
            _ => string.Empty,
        };
        return new TransactionRow(
            t,
            t.Date.ToString("d MMM, ddd", QuickEntryFormatter.Russian),
            sign + QuickEntryFormatter.Money(t.BaseAmount, baseCurrency),
            t.Currency == baseCurrency ? null : QuickEntryFormatter.Money(t.Amount, t.Currency),
            categories.GetValueOrDefault(t.CategoryId, "?"),
            accounts.GetValueOrDefault(t.AccountId, "?"),
            t.Note);
    }
}

/// <summary>A totals card: «Расходы 12 300 ₽, +1 200 ₽ к прошлому месяцу».</summary>
public sealed record TotalCard(string Title, string AmountText, string ChangeText, bool ChangeIsGood, bool HasChange)
{
    /// <param name="moreIsGood">Higher income or balance is good; higher expenses are not.</param>
    public static TotalCard Create(string title, decimal current, decimal previous, bool moreIsGood, CurrencyCode currency)
    {
        var change = current - previous;
        var changeText = change == 0
            ? "как в прошлом месяце"
            : $"{(change > 0 ? "+" : "−")}{QuickEntryFormatter.Money(Math.Abs(change), currency)} к прошлому месяцу";
        return new TotalCard(title, QuickEntryFormatter.Money(current, currency), changeText, (change > 0) == moreIsGood, change != 0);
    }
}

/// <summary>A horizontal bar of the «expenses by category» chart; <see cref="Share"/> is 0..1 of the biggest category.</summary>
public sealed record CategoryBar(string Name, string AmountText, string ShareText, double Share, string? ChangeText)
{
    public static IReadOnlyList<CategoryBar> Create(MonthSummary summary, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var current = summary.ExpensesByCategory.Where(c => c.Amount > 0).ToList();
        var max = current.Count == 0 ? 0m : current.Max(c => c.Amount);
        return current
            .Select(c => new CategoryBar(
                c.CategoryName,
                QuickEntryFormatter.Money(c.Amount, currency),
                summary.ShareOf(c).ToString("P0", QuickEntryFormatter.Russian),
                max == 0 ? 0 : (double)(c.Amount / max),
                c.PreviousAmount == 0 ? null : $"{(c.Change >= 0 ? "+" : "−")}{QuickEntryFormatter.Money(Math.Abs(c.Change), currency)}"))
            .ToList();
    }
}

public static class MonthTitle
{
    /// <summary>«Октябрь 2026».</summary>
    public static string Of(YearMonth month)
    {
        var name = QuickEntryFormatter.Russian.DateTimeFormat.MonthNames[month.Month - 1];
        return string.Create(CultureInfo.InvariantCulture, $"{char.ToUpper(name[0], QuickEntryFormatter.Russian)}{name[1..]} {month.Year}");
    }
}
