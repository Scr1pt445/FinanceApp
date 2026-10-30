using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Application.Accounts;

/// <summary>A category's monthly limit and what is spent of it, both in the base currency.</summary>
public sealed record LimitLine(Category Category, decimal Limit, decimal Spent)
{
    public decimal Left => Limit - Spent;

    /// <summary>0..∞: over 1 means the limit is exceeded.</summary>
    public decimal Share => Limit == 0 ? 0 : Spent / Limit;

    /// <summary>From 80 % on the line is worth a warning.</summary>
    public bool IsNear => Share >= 0.8m;

    public bool IsOver => Spent > Limit;
}

/// <summary>The month's limits: one line per expense category with a limit, the closest to it first.</summary>
public sealed record MonthLimits(YearMonth Month, IReadOnlyList<LimitLine> Lines, CurrencyCode BaseCurrency);

/// <summary>Spending per category against its monthly limit (requirements: «лимиты по категориям»).</summary>
public sealed class LimitsService(ICategoryRepository categories, ITransactionRepository transactions, ISettingsStore settings)
{
    public async Task<MonthLimits> LoadAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var limited = (await categories.ListAsync(cancellationToken))
            .Where(c => c.Type == TransactionType.Expense && c.MonthlyLimit is not null && !c.IsHidden)
            .ToList();
        var baseCurrency = settings.Load().BaseCurrency;
        if (limited.Count == 0)
        {
            return new MonthLimits(month, [], baseCurrency);
        }

        var spent = (await transactions.ListAsync(month.FirstDay, month.LastDay, cancellationToken))
            .Where(t => t.Type == TransactionType.Expense && t.CategoryId is not null)
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.BaseAmount));
        var lines = limited
            .Select(c => new LimitLine(c, c.MonthlyLimit!.Value, spent.GetValueOrDefault(c.Id)))
            .OrderByDescending(l => l.Share)
            .ThenBy(l => l.Category.SortOrder)
            .ToList();
        return new MonthLimits(month, lines, baseCurrency);
    }
}
