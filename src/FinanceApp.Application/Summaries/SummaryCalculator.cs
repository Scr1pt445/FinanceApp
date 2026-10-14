using FinanceApp.Application.Csv;
using FinanceApp.Domain;

namespace FinanceApp.Application.Summaries;

/// <summary>Income, expense and saving for a period, in the base currency.</summary>
public sealed record MonthTotals(decimal Income, decimal Expense, decimal Saving)
{
    public static MonthTotals Empty { get; } = new(0, 0, 0);

    /// <summary>What is left: income − expense − saving (same formula as the Obsidian page).</summary>
    public decimal Balance => Income - Expense - Saving;

    public MonthTotals Add(TransactionType type, decimal amount) =>
        type switch
        {
            TransactionType.Income => this with { Income = Income + amount },
            TransactionType.Expense => this with { Expense = Expense + amount },
            TransactionType.Saving => this with { Saving = Saving + amount },
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
}

/// <summary>Expenses of one category in the month and in the previous month.</summary>
public sealed record CategoryTotal(Guid CategoryId, string CategoryName, decimal Amount, decimal PreviousAmount)
{
    public decimal Change => Amount - PreviousAmount;
}

public sealed record MonthSummary(
    YearMonth Month,
    MonthTotals Current,
    MonthTotals Previous,
    IReadOnlyList<CategoryTotal> ExpensesByCategory)
{
    /// <summary>Share of the category in the month's expenses, 0..1.</summary>
    public decimal ShareOf(CategoryTotal category) =>
        Current.Expense == 0 ? 0 : category.Amount / Current.Expense;
}

/// <summary>
/// Pure summary math. Totals are computed in memory from base-currency amounts (ADR 0007),
/// so the same code checks imported data against the source file.
/// </summary>
public static class SummaryCalculator
{
    public static MonthSummary ForMonth(
        IEnumerable<Transaction> transactions,
        YearMonth month,
        IReadOnlyDictionary<Guid, Category> categories)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(categories);

        var previousMonth = month.Previous;
        var current = MonthTotals.Empty;
        var previous = MonthTotals.Empty;
        var byCategory = new Dictionary<Guid, (decimal Current, decimal Previous)>();

        foreach (var transaction in transactions)
        {
            var isCurrent = month.Contains(transaction.Date);
            var isPrevious = !isCurrent && previousMonth.Contains(transaction.Date);
            if (!isCurrent && !isPrevious)
            {
                continue;
            }

            var amount = transaction.BaseAmount;
            if (isCurrent)
            {
                current = current.Add(transaction.Type, amount);
            }
            else
            {
                previous = previous.Add(transaction.Type, amount);
            }

            if (transaction.Type == TransactionType.Expense)
            {
                var totals = byCategory.GetValueOrDefault(transaction.CategoryId);
                byCategory[transaction.CategoryId] = isCurrent
                    ? (totals.Current + amount, totals.Previous)
                    : (totals.Current, totals.Previous + amount);
            }
        }

        var expensesByCategory = byCategory
            .Select(pair => new CategoryTotal(
                pair.Key,
                categories.TryGetValue(pair.Key, out var category) ? category.Name : "?",
                pair.Value.Current,
                pair.Value.Previous))
            .OrderByDescending(c => c.Amount)
            .ThenByDescending(c => c.PreviousAmount)
            .ThenBy(c => c.CategoryName, StringComparer.CurrentCulture)
            .ToList();

        return new MonthSummary(month, current, previous, expensesByCategory);
    }

    public static SortedDictionary<YearMonth, MonthTotals> ByMonth(IEnumerable<Transaction> transactions) =>
        ByMonth(transactions.Select(t => (t.Date, t.Type, t.BaseAmount)));

    public static SortedDictionary<YearMonth, MonthTotals> ByMonth(IEnumerable<TransactionCsvRow> rows) =>
        ByMonth(rows.Select(r => (r.Date, r.Type, r.Amount)));

    private static SortedDictionary<YearMonth, MonthTotals> ByMonth(
        IEnumerable<(DateOnly Date, TransactionType Type, decimal Amount)> flows)
    {
        var result = new SortedDictionary<YearMonth, MonthTotals>();
        foreach (var (date, type, amount) in flows)
        {
            var month = YearMonth.Of(date);
            result[month] = result.GetValueOrDefault(month, MonthTotals.Empty).Add(type, amount);
        }

        return result;
    }
}
