using FinanceApp.Application.Accounts;
using FinanceApp.Domain;

namespace FinanceApp.Application.Summaries;

/// <summary>Money of one category in both months, in the base currency.</summary>
public sealed record CategoryComparison(Guid CategoryId, string Name, decimal Amount, decimal OtherAmount)
{
    public decimal Change => Amount - OtherAmount;
}

/// <summary>A recurring operation still to come this month (or due and not recorded yet), in the base currency.</summary>
public sealed record PlannedItem(RecurringOperation Operation, DateOnly Date, decimal BaseAmount);

/// <summary>
/// A category limit at the current pace: <see cref="Projected"/> by the end of the month, the day it runs out if it
/// will, and how much a day is left to stay within it.
/// </summary>
public sealed record LimitForecast(LimitLine Line, decimal Projected, DateOnly? RunsOutOn, decimal PerDayLeft)
{
    public bool WillExceed => !Line.IsOver && Projected > Line.Limit;
}

/// <summary>
/// The rest of the current month. «Свободно» is income − spending from regular accounts − money put into
/// envelopes, as in the summaries; recurring operations still to come are counted in.
/// </summary>
/// <param name="DaysLeft">Today included.</param>
/// <param name="Totals">So far.</param>
/// <param name="PlannedIncome">Recurring income still to come.</param>
/// <param name="PlannedExpense">Recurring expenses from regular accounts still to come.</param>
/// <param name="PlannedSaving">Recurring transfers into envelopes still to come.</param>
/// <param name="ExpectedExpense">All spending by the end of the month: the pace or the planned bills, the larger.</param>
/// <param name="ExpectedBalance">Free money by the end of the month.</param>
/// <param name="PerDay">What can be spent a day till the month ends; <c>null</c> without any income this month.</param>
public sealed record MonthForecast(
    int DaysLeft,
    PeriodTotals Totals,
    decimal PlannedIncome,
    decimal PlannedExpense,
    decimal PlannedSaving,
    decimal ExpectedExpense,
    decimal ExpectedBalance,
    decimal? PerDay,
    IReadOnlyList<PlannedItem> Planned,
    IReadOnlyList<LimitForecast> Limits);

/// <summary>
/// Two months side by side (requirements: «сравнить месяцы»), and for the current one — where it is heading.
/// </summary>
/// <param name="Through">
/// The day of the month both are counted to when one of them is the current month and they are compared «по то же
/// число»; <c>null</c> — both months whole.
/// </param>
public sealed record MonthComparison(
    YearMonth Month,
    YearMonth Other,
    int? Through,
    PeriodTotals Totals,
    PeriodTotals OtherTotals,
    IReadOnlyList<CategoryComparison> Expenses,
    IReadOnlyList<CategoryComparison> Incomes,
    bool OtherHasData,
    MonthForecast? Forecast,
    CurrencyCode BaseCurrency)
{
    /// <summary>Pure math over base-currency amounts.</summary>
    /// <param name="transactions">Must cover both months and the month before <paramref name="month"/> (for the pace).</param>
    /// <param name="samePoint">Count both to today's day of the month when one of them is the current month.</param>
    /// <param name="recurring">Recurring operations, for the forecast.</param>
    /// <param name="limits">The limits of <paramref name="month"/>, for the forecast.</param>
    /// <param name="toBase">How much one unit of a currency is in the base one; <c>null</c> — unknown, the item is left out.</param>
    public static MonthComparison For(
        IEnumerable<Transaction> transactions,
        YearMonth month,
        YearMonth other,
        bool samePoint,
        IReadOnlyDictionary<Guid, Category> categories,
        IReadOnlyDictionary<Guid, Account> accounts,
        IEnumerable<RecurringOperation> recurring,
        MonthLimits? limits,
        Func<CurrencyCode, decimal?> toBase,
        CurrencyCode baseCurrency,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(recurring);
        ArgumentNullException.ThrowIfNull(toBase);

        var all = transactions.ToList();
        var current = YearMonth.Of(today);
        int? through = samePoint && (month == current || other == current) && month != other ? today.Day : null;

        bool In(YearMonth m, DateOnly date) => m.Contains(date) && (through is not { } day || date.Day <= day);

        var mine = all.Where(t => In(month, t.Date)).ToList();
        var theirs = all.Where(t => In(other, t.Date)).ToList();
        var totals = Sum(mine, accounts);
        var otherTotals = Sum(theirs, accounts);

        MonthForecast? forecast = null;
        if (month == current)
        {
            var insights = PeriodInsights.For(all, Period.Of(month), categories, accounts, today);
            var monthTotals = Sum(all.Where(t => month.Contains(t.Date)), accounts);
            forecast = Project(monthTotals, insights.Forecast, month, recurring, accounts, limits, toBase, today);
        }

        return new MonthComparison(
            month,
            other,
            through,
            totals,
            otherTotals,
            ByCategory(mine, theirs, TransactionType.Expense, categories),
            ByCategory(mine, theirs, TransactionType.Income, categories),
            theirs.Count > 0,
            forecast,
            baseCurrency);
    }

    private static PeriodTotals Sum(IEnumerable<Transaction> transactions, IReadOnlyDictionary<Guid, Account> accounts) =>
        transactions.Aggregate(PeriodTotals.Empty, (totals, t) => totals.Add(t, accounts));

    /// <summary>Every category spent in either month, the biggest first.</summary>
    private static List<CategoryComparison> ByCategory(
        List<Transaction> mine, List<Transaction> theirs, TransactionType type, IReadOnlyDictionary<Guid, Category> categories)
    {
        Dictionary<Guid, decimal> Group(IEnumerable<Transaction> list) => list
            .Where(t => t.Type == type && t.CategoryId is not null)
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.BaseAmount));

        var a = Group(mine);
        var b = Group(theirs);
        return a.Keys.Union(b.Keys)
            .Select(id => new CategoryComparison(
                id, categories.TryGetValue(id, out var category) ? category.Name : "?", a.GetValueOrDefault(id), b.GetValueOrDefault(id)))
            .OrderByDescending(c => Math.Max(c.Amount, c.OtherAmount))
            .ThenBy(c => c.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static MonthForecast Project(
        PeriodTotals totals,
        decimal? pace,
        YearMonth month,
        IEnumerable<RecurringOperation> recurring,
        IReadOnlyDictionary<Guid, Account> accounts,
        MonthLimits? limits,
        Func<CurrencyCode, decimal?> toBase,
        DateOnly today)
    {
        bool IsEnvelope(Guid? id) => id is { } value && accounts.TryGetValue(value, out var account) && account.IsEnvelope;

        var planned = new List<PlannedItem>();
        foreach (var operation in recurring)
        {
            if (!accounts.TryGetValue(operation.AccountId, out var account) || toBase(account.Currency) is not { } rate)
            {
                continue;
            }

            // Into an envelope from a regular account is saving; other transfers move nothing that counts.
            if (operation.Type == TransactionType.Transfer && (IsEnvelope(operation.AccountId) || !IsEnvelope(operation.ToAccountId)))
            {
                continue;
            }

            var amount = decimal.Round(operation.Amount * rate, 2, MidpointRounding.AwayFromZero);
            for (var date = operation.NextDate; date <= month.LastDay; date = RecurringOperation.After(date, operation.Frequency, operation.Anchor))
            {
                if (date >= month.FirstDay)
                {
                    planned.Add(new PlannedItem(operation, date, amount));
                }
            }
        }

        planned.Sort((x, y) => x.Date.CompareTo(y.Date));
        var plannedIncome = planned.Where(p => p.Operation.Type == TransactionType.Income).Sum(p => p.BaseAmount);
        var plannedBills = planned.Where(p => p.Operation.Type == TransactionType.Expense).ToList();
        var plannedExpense = plannedBills.Where(p => !IsEnvelope(p.Operation.AccountId)).Sum(p => p.BaseAmount);
        var plannedSaving = planned.Where(p => p.Operation.Type == TransactionType.Transfer).Sum(p => p.BaseAmount);

        // The pace already counts last month's bills; the planned ones make sure the known ones are in.
        var expected = Math.Max(pace ?? totals.Expense, totals.Expense + plannedBills.Sum(p => p.BaseAmount));
        var income = totals.Income + plannedIncome;
        var balance = income - (expected - totals.ExpenseFromEnvelopes) - totals.IntoEnvelopes - plannedSaving;

        var daysLeft = month.LastDay.DayNumber - today.DayNumber + 1;
        var free = income - (totals.Expense - totals.ExpenseFromEnvelopes) - totals.IntoEnvelopes - plannedExpense - plannedSaving;
        decimal? perDay = income > 0 ? decimal.Round(free / daysLeft, 0, MidpointRounding.AwayFromZero) : null;

        var elapsed = today.Day;
        var days = month.LastDay.Day;
        var limitLines = (limits?.Lines ?? []).Select(line =>
        {
            var perDaySpent = line.Spent / elapsed;
            var projected = decimal.Round(line.Spent + (perDaySpent * (days - elapsed)), 0, MidpointRounding.AwayFromZero);
            DateOnly? runsOut = null;
            if (!line.IsOver && perDaySpent > 0 && projected > line.Limit)
            {
                var day = today.AddDays((int)Math.Ceiling(line.Left / perDaySpent));
                runsOut = day > month.LastDay ? month.LastDay : day;
            }

            var left = line.Left > 0 ? decimal.Round(line.Left / daysLeft, 0, MidpointRounding.AwayFromZero) : 0;
            return new LimitForecast(line, projected, runsOut, left);
        }).ToList();

        return new MonthForecast(
            daysLeft,
            totals,
            plannedIncome,
            plannedExpense,
            plannedSaving,
            decimal.Round(expected, 0, MidpointRounding.AwayFromZero),
            decimal.Round(balance, 0, MidpointRounding.AwayFromZero),
            perDay,
            planned,
            limitLines);
    }
}
