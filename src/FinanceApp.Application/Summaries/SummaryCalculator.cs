using FinanceApp.Application.Csv;
using FinanceApp.Domain;

namespace FinanceApp.Application.Summaries;

/// <summary>Money flows of a period in the base currency (ADR 0010).</summary>
/// <param name="Expense">All spending, including spending paid from envelopes.</param>
/// <param name="IntoEnvelopes">Net transfers from regular accounts into envelopes (taken back = minus).</param>
/// <param name="ExpenseFromEnvelopes">The part of <paramref name="Expense"/> paid from envelopes.</param>
public sealed record PeriodTotals(decimal Income, decimal Expense, decimal IntoEnvelopes, decimal ExpenseFromEnvelopes = 0)
{
    public static PeriodTotals Empty { get; } = new(0, 0, 0);

    /// <summary>
    /// How much free money grew: income − spending from regular accounts − money put into envelopes.
    /// Spending from an envelope does not count twice: that money was put aside earlier.
    /// </summary>
    public decimal Balance => Income - (Expense - ExpenseFromEnvelopes) - IntoEnvelopes;

    internal PeriodTotals Add(Transaction t, IReadOnlyDictionary<Guid, Account> accounts)
    {
        var amount = t.BaseAmount;
        return t.Type switch
        {
            TransactionType.Income => this with { Income = Income + amount },
            TransactionType.Expense when IsEnvelope(t.AccountId, accounts) =>
                this with { Expense = Expense + amount, ExpenseFromEnvelopes = ExpenseFromEnvelopes + amount },
            TransactionType.Expense => this with { Expense = Expense + amount },
            TransactionType.Saving => this with { IntoEnvelopes = IntoEnvelopes + amount }, // legacy rows
            TransactionType.Transfer => this with
            {
                IntoEnvelopes = IntoEnvelopes + (IsEnvelope(t.ToAccountId, accounts) == IsEnvelope(t.AccountId, accounts)
                    ? 0
                    : IsEnvelope(t.ToAccountId, accounts) ? amount : -amount),
            },
            _ => this,
        };
    }

    internal PeriodTotals Add(TransactionCsvRow row, Func<string?, bool> isEnvelope) =>
        row.Type switch
        {
            TransactionType.Income => this with { Income = Income + row.Amount },
            TransactionType.Expense when isEnvelope(row.Account) =>
                this with { Expense = Expense + row.Amount, ExpenseFromEnvelopes = ExpenseFromEnvelopes + row.Amount },
            TransactionType.Expense => this with { Expense = Expense + row.Amount },
            TransactionType.Saving => this with { IntoEnvelopes = IntoEnvelopes + row.Amount },
            TransactionType.Transfer => this with
            {
                IntoEnvelopes = IntoEnvelopes + (isEnvelope(row.ToAccount) == isEnvelope(row.Account)
                    ? 0
                    : isEnvelope(row.ToAccount) ? row.Amount : -row.Amount),
            },
            _ => this,
        };

    private static bool IsEnvelope(Guid? accountId, IReadOnlyDictionary<Guid, Account> accounts) =>
        accountId is { } id && accounts.TryGetValue(id, out var account) && account.IsEnvelope;
}

/// <summary>Expenses of one category in the period and in the previous one.</summary>
public sealed record CategoryTotal(Guid CategoryId, string CategoryName, decimal Amount, decimal PreviousAmount)
{
    public decimal Change => Amount - PreviousAmount;
}

public sealed record PeriodSummary(
    Period Period,
    PeriodTotals Current,
    PeriodTotals? Previous,
    IReadOnlyList<CategoryTotal> ExpensesByCategory,
    IReadOnlyList<(YearMonth Month, PeriodTotals Totals)> ByMonth,
    decimal AverageDailyExpense)
{
    /// <summary>Share of the category in the period's expenses, 0..1.</summary>
    public decimal ShareOf(CategoryTotal category) =>
        Current.Expense == 0 ? 0 : category.Amount / Current.Expense;
}

/// <summary>
/// Pure summary math. Totals are computed in memory from base-currency amounts (ADR 0007),
/// so the same code checks imported data against the source file.
/// </summary>
public static class SummaryCalculator
{
    /// <param name="transactions">Must cover the period and the previous one.</param>
    /// <param name="today">Ongoing periods average spending over the days already passed.</param>
    public static PeriodSummary For(
        IEnumerable<Transaction> transactions,
        Period period,
        IReadOnlyDictionary<Guid, Category> categories,
        IReadOnlyDictionary<Guid, Account> accounts,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(accounts);

        var previousPeriod = period.Previous;
        var current = PeriodTotals.Empty;
        var previous = PeriodTotals.Empty;
        var byCategory = new Dictionary<Guid, (decimal Current, decimal Previous)>();
        var byMonth = new SortedDictionary<YearMonth, PeriodTotals>();
        DateOnly? first = null;

        foreach (var transaction in transactions)
        {
            var isCurrent = period.Contains(transaction.Date);
            var isPrevious = !isCurrent && previousPeriod?.Contains(transaction.Date) == true;
            if (!isCurrent && !isPrevious)
            {
                continue;
            }

            if (isCurrent)
            {
                current = current.Add(transaction, accounts);
                var month = YearMonth.Of(transaction.Date);
                byMonth[month] = byMonth.GetValueOrDefault(month, PeriodTotals.Empty).Add(transaction, accounts);
                first = first is null || transaction.Date < first ? transaction.Date : first;
            }
            else
            {
                previous = previous.Add(transaction, accounts);
            }

            if (transaction is { Type: TransactionType.Expense, CategoryId: { } categoryId })
            {
                var totals = byCategory.GetValueOrDefault(categoryId);
                byCategory[categoryId] = isCurrent
                    ? (totals.Current + transaction.BaseAmount, totals.Previous)
                    : (totals.Current, totals.Previous + transaction.BaseAmount);
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

        // Months without transactions are shown as zeros, so a chart has no gaps.
        var months = period.Kind == PeriodKind.AllTime
            ? (first is { } start ? period.Months(start, today) : [])
            : period.Months(clampEnd: today >= period.Start ? today : period.End);
        var monthly = months.Select(m => (m, byMonth.GetValueOrDefault(m, PeriodTotals.Empty))).ToList();

        var daysStart = period.Kind == PeriodKind.AllTime ? first ?? today : period.Start;
        var daysEnd = period.End < today ? period.End : today;
        var days = Math.Max(daysEnd.DayNumber - daysStart.DayNumber + 1, 1);
        var averageDaily = daysEnd < daysStart ? 0 : decimal.Round(current.Expense / days, 2, MidpointRounding.AwayFromZero);

        return new PeriodSummary(
            period,
            current,
            previousPeriod is null ? null : previous,
            expensesByCategory,
            monthly,
            averageDaily);
    }

    public static SortedDictionary<YearMonth, PeriodTotals> ByMonth(
        IEnumerable<Transaction> transactions,
        IReadOnlyDictionary<Guid, Account> accounts)
    {
        var result = new SortedDictionary<YearMonth, PeriodTotals>();
        foreach (var t in transactions)
        {
            var month = YearMonth.Of(t.Date);
            result[month] = result.GetValueOrDefault(month, PeriodTotals.Empty).Add(t, accounts);
        }

        return result;
    }

    /// <param name="isEnvelope">Whether an account name from the file is an envelope (for transfer rows).</param>
    public static SortedDictionary<YearMonth, PeriodTotals> ByMonth(
        IEnumerable<TransactionCsvRow> rows,
        Func<string?, bool> isEnvelope)
    {
        var result = new SortedDictionary<YearMonth, PeriodTotals>();
        foreach (var row in rows)
        {
            var month = YearMonth.Of(row.Date);
            result[month] = result.GetValueOrDefault(month, PeriodTotals.Empty).Add(row, isEnvelope);
        }

        return result;
    }
}
