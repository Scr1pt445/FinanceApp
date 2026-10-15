using FinanceApp.Domain;

namespace FinanceApp.Application.Summaries;

/// <summary>How a spending chart groups a period: by day, by week or by month.</summary>
public enum BucketSize
{
    Day,
    Week,
    Month,
}

/// <summary>One column of the spending chart: expenses from <see cref="Start"/> to <see cref="End"/>.</summary>
public sealed record SpendBucket(DateOnly Start, DateOnly End, decimal Expense);

/// <summary>Money of one category in the period, in the base currency.</summary>
public sealed record CategoryAmount(Guid CategoryId, string CategoryName, decimal Amount);

/// <summary>
/// What the summary page shows beyond the totals: where income went, the pace of spending against the
/// previous period, a forecast, the biggest expenses, income sources and envelope movements.
/// </summary>
/// <param name="SpentFromAccountsByCategory">
/// Expenses paid from regular accounts — what income was actually spent on. Spending from envelopes is
/// money put aside earlier and is reported separately.
/// </param>
/// <param name="Buckets">Chart columns from the start of the period up to its end (empty ones included).</param>
/// <param name="Cumulative">Running total of expenses per bucket, up to today for an ongoing period.</param>
/// <param name="PreviousCumulative">
/// The same for the previous period, aligned by bucket index; empty when there is no previous period or it
/// has no transactions at all (before the user started tracking) — then there is nothing to compare with.
/// </param>
/// <param name="ElapsedDays">Days of the period already passed (all of them for a finished period).</param>
/// <param name="PreviousToSamePoint">Expenses of the previous period over the same number of days; <c>null</c> without history.</param>
/// <param name="Forecast">Expected expenses by the end of an ongoing period; <c>null</c> for a finished one.</param>
/// <param name="EnvelopeFlows">Net movement of each envelope during the period, in the envelope currency.</param>
public sealed record PeriodInsights(
    IReadOnlyList<CategoryAmount> SpentFromAccountsByCategory,
    BucketSize BucketSize,
    IReadOnlyList<SpendBucket> Buckets,
    IReadOnlyList<decimal> Cumulative,
    IReadOnlyList<decimal> PreviousCumulative,
    int ElapsedDays,
    decimal? PreviousToSamePoint,
    decimal? Forecast,
    IReadOnlyList<Transaction> TopExpenses,
    IReadOnlyList<CategoryAmount> IncomeByCategory,
    IReadOnlyDictionary<Guid, decimal> EnvelopeFlows)
{
    public bool IsOngoing => Forecast is not null;

    /// <summary>Pure math over the transactions of the period and the previous one (base-currency amounts).</summary>
    /// <param name="transactions">Must cover the period and the previous one.</param>
    public static PeriodInsights For(
        IEnumerable<Transaction> transactions,
        Period period,
        IReadOnlyDictionary<Guid, Category> categories,
        IReadOnlyDictionary<Guid, Account> accounts,
        DateOnly today,
        int topCount = 5)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(accounts);

        var all = transactions.ToList();
        var current = all.Where(t => period.Contains(t.Date)).ToList();
        var previousPeriod = period.Previous;
        var previous = previousPeriod is null ? [] : all.Where(t => previousPeriod.Contains(t.Date)).ToList();
        var hasHistory = previous.Count > 0;

        // All time starts at the first transaction; a period never runs past today for the chart.
        var start = period.Kind == PeriodKind.AllTime ? current.Select(t => t.Date).DefaultIfEmpty(today).Min() : period.Start;
        var end = period.Kind == PeriodKind.AllTime ? today : period.End;
        var ongoing = period.Kind != PeriodKind.AllTime && today >= start && today <= end;
        var lastDay = end < today ? end : today;
        var elapsed = Math.Max(lastDay.DayNumber - start.DayNumber + 1, 0);

        var size = BucketFor(end.DayNumber - start.DayNumber + 1);
        var buckets = Bucketize(current, start, end, size);
        var cumulative = Accumulate(buckets.Where(b => b.Start <= lastDay));
        var previousCumulative = previousPeriod is null || !hasHistory
            ? []
            : Accumulate(Bucketize(previous, previousPeriod.Start, previousPeriod.End, size));

        decimal? toSamePoint = null;
        if (previousPeriod is not null && hasHistory)
        {
            var sameDay = previousPeriod.Start.AddDays(elapsed - 1);
            toSamePoint = previous.Where(t => t.Type == TransactionType.Expense && t.Date <= sameDay).Sum(t => t.BaseAmount);
        }

        var spent = current.Where(t => t.Type == TransactionType.Expense).Sum(t => t.BaseAmount);
        decimal? forecast = null;
        if (ongoing)
        {
            var previousTotal = previous.Where(t => t.Type == TransactionType.Expense).Sum(t => t.BaseAmount);

            // The rest of the period like the rest of the previous one: rent paid on the 3rd does not repeat.
            // Without history, extrapolate the pace since the first operation (tracking may start mid-year).
            if (previousTotal > 0 && toSamePoint is { } samePoint)
            {
                forecast = spent + (previousTotal - samePoint);
            }
            else
            {
                var first = current.Select(t => t.Date).Where(d => d <= lastDay).DefaultIfEmpty(start).Min();
                var tracked = Math.Max(lastDay.DayNumber - first.DayNumber + 1, 1);
                var remaining = end.DayNumber - lastDay.DayNumber;
                forecast = decimal.Round(spent + (spent / tracked * remaining), 0, MidpointRounding.AwayFromZero);
            }
        }

        bool IsEnvelope(Guid? id) => id is { } value && accounts.TryGetValue(value, out var account) && account.IsEnvelope;
        string Name(Guid? id) => id is { } value && categories.TryGetValue(value, out var category) ? category.Name : "?";

        var fromAccounts = current
            .Where(t => t.Type == TransactionType.Expense && !IsEnvelope(t.AccountId))
            .GroupBy(t => t.CategoryId)
            .Select(g => new CategoryAmount(g.Key ?? Guid.Empty, Name(g.Key), g.Sum(t => t.BaseAmount)))
            .OrderByDescending(c => c.Amount)
            .ToList();

        var incomes = current
            .Where(t => t.Type == TransactionType.Income)
            .GroupBy(t => t.CategoryId)
            .Select(g => new CategoryAmount(g.Key ?? Guid.Empty, Name(g.Key), g.Sum(t => t.BaseAmount)))
            .OrderByDescending(c => c.Amount)
            .ToList();

        var top = current
            .Where(t => t.Type == TransactionType.Expense)
            .OrderByDescending(t => t.BaseAmount)
            .ThenByDescending(t => t.Date)
            .Take(topCount)
            .ToList();

        var flows = accounts.Values
            .Where(a => a.IsEnvelope)
            .ToDictionary(a => a.Id, a => current.Sum(t => t.EffectOn(a.Id)));

        return new PeriodInsights(
            fromAccounts, size, buckets, cumulative, previousCumulative, elapsed, toSamePoint, forecast, top, incomes, flows);
    }

    private static BucketSize BucketFor(int days) => days switch
    {
        <= 62 => BucketSize.Day,
        <= 190 => BucketSize.Week,
        _ => BucketSize.Month,
    };

    private static List<SpendBucket> Bucketize(List<Transaction> transactions, DateOnly start, DateOnly end, BucketSize size)
    {
        var expenses = transactions.Where(t => t.Type == TransactionType.Expense).ToList();
        var result = new List<SpendBucket>();
        var from = start;
        while (from <= end)
        {
            var to = size switch
            {
                BucketSize.Day => from,
                BucketSize.Week => from.AddDays(6),
                _ => new DateOnly(from.Year, from.Month, 1).AddMonths(1).AddDays(-1),
            };
            to = to > end ? end : to;
            var bucketStart = from;
            result.Add(new SpendBucket(from, to, expenses.Where(t => t.Date >= bucketStart && t.Date <= to).Sum(t => t.BaseAmount)));
            from = to.AddDays(1);
        }

        return result;
    }

    private static List<decimal> Accumulate(IEnumerable<SpendBucket> buckets)
    {
        var result = new List<decimal>();
        var sum = 0m;
        foreach (var bucket in buckets)
        {
            sum += bucket.Expense;
            result.Add(sum);
        }

        return result;
    }
}
